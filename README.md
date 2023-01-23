# WebVTTStreamReader

WebVTTStreamReader allows you to get a subtitle stream of an IPTV channel or a HLS stream.
The stream listener will run on background thread.

In order for the library to work you need to pass the subtitle m3u8 url, you can find it by opening the main m3u8 url of the IPTV.

To install this library execute this command in your C# project :
```sh
dotnet add package WebVTTStreamReader
```

Example :
```cs
static void Main()
{
    SubStreamReader subStream = new SubStreamReader(
        "https://ndrint.akamaized.net/hls/live/2020766/ndr_int/master-subs.m3u8",
        timeInitOffsetInMils: 3000,
        initDelayToRefreshInSec: 5
    );

    subStream.OnUpdateSubtitle += () =>
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(args.StartTimeStamp + " --> " + args.EndTimeStamp + " : " + DateTime.UtcNow);
        Console.ForegroundColor = ConsoleColor.White;

        // Going through the subtitle block
        foreach (string item in args.BlockTexts)
        {
            Console.WriteLine(item);
        }
        Console.WriteLine();
    };

    subStream.RunStreamListener();

    while(Console.ReadKey().Key != ConsoleKey.Q);
}
```
This code will display the subtitles in the console as they are received. 
 
  
