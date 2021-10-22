using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using System.IO;

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
        private int delayToRefresh; 
        private int timeout;
        private DateTime lastTimestamp;
        private double delayToRaiseEvent;
        private bool run;
        private Thread streamListenerThread;

        public SubStreamReader(string url, double timeInitOffsetInMils, int initDelayToRefreshInSec, double delayToRaiseEventInMils = 0, int requestTimeOutInMils = 2000)
        {
            this.url = url; 
            this.delayToRefresh = initDelayToRefreshInSec;
            this.lastTimestamp = DateTime.UtcNow.AddMilliseconds(-timeInitOffsetInMils);
            this.delayToRaiseEvent = delayToRaiseEventInMils;
            this.timeout = requestTimeOutInMils;
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

        public void RunStreamListener()
        {
            this.run = true;
            this.streamListenerThread = new Thread(InvokeListenStream); 
            this.streamListenerThread.IsBackground = true;
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
        private void InvokeListenStream()
        {
            this.ListenStream().GetAwaiter().GetResult();
        }

        private async Task ListenStream()
        {
            while(this.run)
            {
                Stream resStream;
                try
                {
                    WebRequest req = WebRequest.Create(this.url);
                    req.Timeout = this.timeout;
                    resStream = req.GetResponse().GetResponseStream();
                    // Display.Info("Request success");
                }
                catch(System.Net.WebException)
                {
                    Console.WriteLine("[" + DateTime.Now.TimeOfDay + " ] Failed to fetch main M3U8 file");
                    Thread.Sleep(this.delayToRefresh * 1000);
                    continue;
                }
                DateTime requestTime = DateTime.UtcNow;

                await this.ReadStream(resStream);
                
                // Display.Info( "Sleeping :  " + this.delayToRefresh * 1000);
                Thread.Sleep(this.delayToRefresh * 1000);
            }
        }

        private async Task ReadStream(Stream stream)
        {
            bool timeFound = false;
            DateTime lastFoundTimestamp = this.lastTimestamp; 
            double duration = 6;
            
            using(StreamReader sr = new StreamReader(stream))
            {
                while(sr.Peek() >=  0)
                {
                    string currentLine = await sr.ReadLineAsync();
                    if(currentLine.StartsWith(TIMESTAMP_KEY_NAME))
                    {
                        DateTime currentTimeStamp = DateTime.Parse(currentLine.Replace(TIMESTAMP_KEY_NAME, "")).ToUniversalTime();
                        if(lastFoundTimestamp < currentTimeStamp)
                        {
                            lastFoundTimestamp =  currentTimeStamp;
                            timeFound = true; 
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
                        if(currentLine.StartsWith("http"))
                        {
                            await this.FetchSubtitlesBlock(currentLine, lastFoundTimestamp, duration);   
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
                stream = req.GetResponse().GetResponseStream();
            }
            catch(System.Net.WebException)
            {
                Console.WriteLine("[" + DateTime.Now.TimeOfDay + "Failed to fetch subtitle : " + url);
                return;
            }

            List<string> blockTextList = new List<string>();

            using(StreamReader sr = new StreamReader(stream))
            {
                bool hasTextFromHere = false;
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
                            blockTextList.Add(currentBlockText);
                            currentBlockText = "";
                            continue;
                        }
                        currentBlockText += currentLine; 
                    }
                }
            }

            Task.Run(() => {

                DateTime timeTowaitTo = startTimestamp.AddMilliseconds(this.delayToRaiseEvent); 
                int timeToWaitMs = (int) timeTowaitTo.Subtract(DateTime.UtcNow).TotalMilliseconds;
                
                if(timeToWaitMs > 0)
                    Thread.Sleep(timeToWaitMs);

                this.OnUpdateSubtitle(
                    this,
                    new UpdateSubtitleEventArgs(
                        blockTextList.ToArray(), 
                        startTimestamp, 
                        startTimestamp.AddSeconds(duration)
                    )
                );
            }); 

        }
    }
}
