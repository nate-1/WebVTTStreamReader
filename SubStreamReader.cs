using System;
using System.Net;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace WebVTTStreamReader
{
    public delegate void UpdateSubtitle(object source, UpdateSubtitleEventArgs e);
    public class UpdateSubtitleEventArgs : EventArgs
    {
        private string[] blockTexts;
        private DateTime startTimeStamp; 
        private DateTime endTimeStamp; 

        public UpdateSubtitleEventArgs(string[] blockTexts, DateTime startTimeStamp, DateTime endTimeStamp)
        {
            this.blockTexts = blockTexts; 
            this.startTimeStamp = startTimeStamp; 
            this.endTimeStamp = endTimeStamp; 
        }

        public string[] BlockTexts
        {
            get { return this.blockTexts; }
        }
        public DateTime StartTimeStamp
        {
            get { return this.startTimeStamp; }
        }
        public DateTime EndTimeStamp
        {
            get { return this.endTimeStamp; }
        }
    }

    public class SubStreamReader
    {
        const string TIMESTAMP_KEY_NAME = "#EXT-X-PROGRAM-DATE-TIME:"; 
        const string DELAY_KEY_NAME = "#EXTINF:";

        public event UpdateSubtitle OnUpdateSubtitle;
        
        private string url; 
        private string urlPath;
        private int delayToRefresh; 
        private int timeout;
        private DateTime lastTimestamp;
        private double delayToRaiseEvent;
        private bool run;
        private Thread streamListenerThread;

        private string lastBlockText;

        public SubStreamReader(string url, double timeInitOffsetInMils, int initDelayToRefreshInSec, double delayToRaiseEventInMils = 0, int requestTimeOutInMils = 2000)
        {
            this.url = url; 
            this.urlPath = url.Substring(0, url.LastIndexOf('/') + 1);
            this.delayToRefresh = initDelayToRefreshInSec;
            this.lastTimestamp = DateTime.UtcNow.AddMilliseconds(-timeInitOffsetInMils);
            this.delayToRaiseEvent = delayToRaiseEventInMils;
            this.timeout = requestTimeOutInMils;
            this.lastBlockText = "";
        }

        public string Url
        {
            get { return this.url; }
        }
        public int RequestsTimeout
        {
            get { return this.timeout; }
            set { this.timeout = value; }
        }
        public double DelayToRaiseEvent
        {
            get { return this.delayToRaiseEvent; }
            set { this.delayToRaiseEvent = value; }
        }
        public Thread StreamListenerThread
        {
            get { return this.streamListenerThread; }
        }
        public bool IsRunning
        {
            get { return this.run; }
        }

        /// <summary>Starting the stream lister thread</summary>
        public void RunStreamListener()
        {
            this.run = true;
            this.streamListenerThread = new Thread(() => this.ListenStream().GetAwaiter().GetResult())
            {
                IsBackground = true
            }; 
            this.streamListenerThread.Start();
        }
        public void StopStreamListener(bool waitForTheThreadToStop = true)
        {
            this.run = false;
            if(waitForTheThreadToStop)
            {                
                while(streamListenerThread.IsAlive || streamListenerThread.ThreadState is not ThreadState.Stopped or ThreadState.Aborted)
                {
                    Thread.Sleep(100);
                }
            }
        }

        private async Task ListenStream()
        {
            while(this.run)
            {
                Stream resStream;
                try
                {
                    DebugLog("Request main");
                    WebRequest req = WebRequest.Create(this.url);
                    req.Timeout = this.timeout;
                    resStream = req.GetResponse().GetResponseStream();
                }
                catch(System.Net.WebException)
                {
                    Console.WriteLine("[" + DateTime.Now.TimeOfDay + "] Failed to fetch main M3U8 file");
                    Thread.Sleep(this.delayToRefresh * 1000);
                    continue;
                }
                await this.ReadStream(resStream);

                DateTime timeToWaitTo = this.lastTimestamp.AddSeconds((this.delayToRefresh * 2) + 1);
                int milsToWait = (int) timeToWaitTo.Subtract(DateTime.UtcNow).TotalMilliseconds;

                if(milsToWait > 0) 
                    Thread.Sleep(milsToWait + 500);
                else 
                    Thread.Sleep(500);
            }
        }

        private async Task ReadStream(Stream stream)
        {
            bool timeFound = false;
            DateTime lastFoundTimestamp = this.lastTimestamp; 
            double duration = 4;
            
            using(StreamReader sr = new StreamReader(stream))
            {
                while(true)
                {
                    string currentLine;
                    try
                    {
                        currentLine = await sr.ReadLineAsync();
                        if(currentLine is null)
                            break;
                    }
                    catch(Exception e)
                    {   
                        Console.WriteLine(e.ToString());
                        if(this.lastTimestamp < lastFoundTimestamp)
                        {
                            this.lastTimestamp =  lastFoundTimestamp;
                            timeFound = true; 
                        }
                        return;
                    }
                    if(currentLine.StartsWith(TIMESTAMP_KEY_NAME))
                    {
                        DateTime currentTimeStamp = DateTime.Parse(currentLine.Replace(TIMESTAMP_KEY_NAME, "")).ToUniversalTime();
                        if(lastFoundTimestamp < currentTimeStamp)
                        {
                            lastFoundTimestamp = currentTimeStamp;
                            timeFound = true; 
                            DebugLog("Time found: " + currentTimeStamp);
                        }
                        continue;
                    }
                    if(timeFound)
                    {
                        if(currentLine.StartsWith(DELAY_KEY_NAME))
                        {
                            duration = Double.Parse(currentLine.Replace(DELAY_KEY_NAME, "").Replace(",", ""));
                            continue;
                        }
                        if(currentLine.EndsWith("vtt"))
                        {
                            string vttUrl = currentLine;
                            if(!vttUrl.StartsWith("http"))
                                vttUrl = this.urlPath + currentLine;

                            await this.FetchSubtitlesBlock(vttUrl, lastFoundTimestamp, duration);   
                        }
                    }
                }
            }

            this.lastTimestamp = lastFoundTimestamp;
        }

        public async Task FetchSubtitlesBlock(string url, DateTime startTimestamp, double duration)
        {
            Stream stream;
            try
            {
                WebRequest req = WebRequest.Create(url);
                req.Timeout = this.timeout;
                stream = req.GetResponse().GetResponseStream();
            }
            catch(System.Net.WebException)
            {
                Console.WriteLine("[" + DateTime.Now.TimeOfDay + "] Failed to fetch subtitle: " + url);
                return;
            }

            List<string> blockTextList = new List<string>();

            using(StreamReader sr = new StreamReader(stream))
            {
                bool hasTextFromHere = false;
                bool isFirstBlock = true;
                string currentBlockText = "";
                while(sr.Peek() >=  0)
                {
                    string currentLine = await sr.ReadLineAsync();
                    
                    if(currentLine.Contains("-->"))
                    {
                        hasTextFromHere = true;
                        continue;
                    }
                    if(hasTextFromHere)
                    {
                        if(String.IsNullOrWhiteSpace(currentLine))
                        {
                            hasTextFromHere = false;
                            if(isFirstBlock && this.lastBlockText == currentBlockText)
                            {
                                isFirstBlock = false;
                                currentBlockText = "";
                                continue;
                            }

                            blockTextList.Add(currentBlockText);
                            this.lastBlockText = currentBlockText;
                            isFirstBlock = false;
                            currentBlockText = "";
                            continue;
                        }
                        
                        currentBlockText += currentLine + " "; 
                    }
                }
            }

            UpdateSubtitleEventArgs subEvent = new UpdateSubtitleEventArgs(
                blockTextList.ToArray(), 
                startTimestamp.AddMilliseconds(this.delayToRaiseEvent), 
                startTimestamp.AddSeconds(duration).AddMilliseconds(this.delayToRaiseEvent)
            );

            if(this.delayToRaiseEvent == 0)
            {
                this.OnUpdateSubtitle(this, subEvent);
            }
            else 
            {
                Task.Run(() => {

                    DateTime timeTowaitTo = startTimestamp.AddMilliseconds(this.delayToRaiseEvent); 
                    int timeToWaitMs = (int) timeTowaitTo.Subtract(DateTime.UtcNow).TotalMilliseconds;
                    
                    if(timeToWaitMs > 0)
                        Thread.Sleep(timeToWaitMs);

                    this.OnUpdateSubtitle(this, subEvent);
                }); 
            }

        }

        private void DebugLog(string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow; 
            Console.WriteLine($"[{DateTime.Now.TimeOfDay}] {message}");
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}
