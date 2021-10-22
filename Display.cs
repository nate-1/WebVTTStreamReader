using System; 

namespace WebVTTStreamReader 
{
    static class Display
    {
        private const string ERROR_PREFIX = "[E] ";
        private const string WARN_PREFIX = "[W] ";
        private const string INFO_PREFIX = "[I] ";
        static public void Error(string value)
        {
            Console.ForegroundColor = ConsoleColor.Red; 
            Console.WriteLine(ERROR_PREFIX + value); 
        }
        static public void Warn(string value)
        {
            Console.ForegroundColor = ConsoleColor.Yellow; 
            Console.WriteLine(WARN_PREFIX + value); 
        }
        static public void Info(string value)
        {
            Console.ForegroundColor = ConsoleColor.White; 
            Console.WriteLine(INFO_PREFIX + value); 
        }
    }
}