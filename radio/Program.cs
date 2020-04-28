using System;
using PlaylistsNET.Content;
using PlaylistsNET.Models;
using System.IO;
using System.Threading;
using System.Diagnostics;
using CSCore.Codecs.MP3;
using CSCore;
using CSCore.Streams;
using CSCore.SoundOut;
using CSCore.Streams.Effects;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using CSCore.CoreAudioAPI;
using CSCore.Codecs;
using CSCore.Tags.ID3;
using CSCore.SoundIn;

namespace m3u
{
    class Program
    {
        static void Main(string[] args)
        {
            new Program().Run();
        }

        float targetVolume = 0.4f,
              leftPitch = 0f,
              rightPitch = 0f;

        string currentPath;
        IWaveSource currentStream;
        PitchShifter pitchShifter;
        WasapiOut wasapiOut = new WasapiOut();
        bool initialized = false;

        const string INTRO_LOGO = "\n\n" +
"                 ,---------------------------," + "\n" +
"                 |  /----------------------\\  |"+ "\n" +
"                 | |        [######]        | |" + "\n" +
"                 | |   /a/ - Animu & Mango  | |" + "\n" +
"                 | |        ,~~~~~~,        | |" + "\n" +
"                 | |        |______|        | |" + "\n" +
"                 | |      ____________      | |" + "\n" +
"                 |  \\______________________/  |" + "\n" +
"                 |____________________________|" + "\n" +
"              ,----\\_____[] _______________/------," + "\n" +
"             /         /______________\\          /|" + "\n" +
"            /__________________________________ / | ___" + "\n" +
"            |                                   | |    )" + "\n" +
"            |  _ _ _               [-------]    | |   (" + "\n" +
"            |  o o o               [-------]    | /   _)_" + "\n" +
"            |__________________________________ |/   /  /" + "\n" +
"       /-------------------------------------/|     ( )/" + "\n" +
"     /-/-/-/-/-/-/-/-/-/-/-/-/-/-/-/-/-/-/-/ /" + "\n" +
"   /-/-/-/-/-/-/-/-/-/-/-/-/-/-/-/-/-/-/-/ /" + "\n" +
"   ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\n\n";

        const string HELP_MESSAGE = "playlist [path] - loads a playlist\n" +
            "play - starts playing the playlist\n" +
            "stop - stops playing the playlist\n" +
            "pause - pauses the playlist\n" +
            "resume - resumes the playlist\n" +
            "thread [board] - attempts to locate & open the current r/a/dio thread(s), [board] defaults to /a/\n" +
            "pos - gives you the current time position\n" +
            "vol [x] - sets the volume to [x] (0.00 - 1.00)";
        private string Repeat(string str, int times)
        {
            times = Math.Abs(times);
            string STR = str;
            for (int i = 0; i < times; ++i)
            {
                STR += str;
            }
            return STR;
        }
        public static string Reverse(string s)
        {
            char[] charArray = s.ToCharArray();
            Array.Reverse(charArray);
            return new string(charArray);
        }
        private void Music()
        {
            const float DELTA = 0.02f;
            string thing = "ya";
            int time = 0;
            Random randomizer = new Random();
            while (true)
            {
                bool mod2 = ((time % 1000) > 500);
                if (initialized)
                {
                    wasapiOut.Volume = Math.Max(0, Math.Min(1, wasapiOut.Volume * (1 - DELTA) + targetVolume * DELTA));

                    float mod = (time % 100);
                    thing = mod2 ? (mod > 75 ? "¯\\_(ツ)_/¯" : (mod > 50) ? "¯--(ツ)--¯" : (mod > 25) ? "_/¯(ツ)¯\\_" : "_--(ツ)--_") : ((mod > 75) ? "ya" : (mod > 50) ? "wub" : (mod > 25) ? "oo" : "ke");
                    Console.Title = "r/a/dio  > " + Repeat(thing, (mod2 ? 1 : 2) * (int)(leftPitch + rightPitch) / (mod2 ? 2 : 1)) + " [ ∞ ]";
                }
                time += mod2 ? 10 : randomizer.Next(2, 6);
                Thread.Sleep(30);
            }
        }
        private bool M3uCheck()
        {
            if (!initialized)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.BackgroundColor = ConsoleColor.Black;
                Console.Write("you need to set playlist first");
                return true;
            }
            return false;
        }
        private int FindPrevious(string str, string pattern)
        {
            for (int i = str.Length; i > pattern.Length; --i)
            {
                if (str.Substring(i - pattern.Length, pattern.Length) == pattern)
                {
                    return i;
                }
            }
            return -1;
        }
        private int FindNext(string str, string pattern)
        {
            for (int i = 0; i < str.Length - pattern.Length; ++i)
            {
                if (str.Substring(i, pattern.Length) == pattern)
                {
                    return i;
                }
            }
            return -1;
        }
        private async Task Cmd(string cmd)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.BackgroundColor = ConsoleColor.Black;
            if (cmd.StartsWith("vol "))
            {
                float.TryParse(cmd.Substring(4), out targetVolume);
                Console.Write("set volume to " + targetVolume.ToString());
                return;
            }
            if (cmd.StartsWith("pos"))
            {
                Console.Write("current pos is " + wasapiOut.WaveSource.GetPosition().ToString());
                return;
            }
            if (cmd.StartsWith("playlist "))
            {
                if (initialized)
                {
                    wasapiOut.Stop();
                    initialized = false;
                }
                string path = cmd.Substring(9);
                bool onComputer = true;
                if (!File.Exists(@path))
                {
                    onComputer = false;
                }
                Stream stream = Stream.Null;
                if (onComputer)
                {
                    stream = File.OpenRead(@path);
                }
                else
                {
                    try
                    {
                        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(path);
                        HttpWebResponse response = (HttpWebResponse)req.GetResponse();
                        stream = response.GetResponseStream();
                    } catch { }
                }
                if (stream == Stream.Null)
                {
                    Console.Write("couldn't read " + path);
                    return;
                }
                string extension = Path.GetExtension(path);
                IPlaylistParser<IBasePlaylist> parser = PlaylistParserFactory.GetPlaylistParser(extension);
                IBasePlaylist playlist = parser.GetFromStream(stream);
                foreach (string str in playlist.GetTracksPaths())
                {
                    currentStream = new Mp3WebStream(str, false);
                    ISampleSource source = currentStream.ToSampleSource().AppendSource(x => new PitchShifter(x), out pitchShifter);
                    var notificationSource = new SingleBlockNotificationStream(source);
                    notificationSource.SingleBlockRead += (s, a) =>
                    {
                        leftPitch = Math.Abs(a.Left) * 10;
                        rightPitch = Math.Abs(a.Right) * 10;
                    };
                    currentStream = notificationSource.ToWaveSource();
                    currentPath = path;
                    wasapiOut.Initialize(currentStream);
                    wasapiOut.Volume = 0.0f;
                    initialized = true;
                }
                Console.Write("set playlist to " + path);
                return;
            }
            if (cmd.StartsWith("thread"))
            {
                string board = "a";
                if (cmd.Length > 6)
                {
                    board = cmd.Substring(7);
                }
                
                Dictionary<int, int> a_threads = new Dictionary<int, int>();
                Dictionary<int, int> smug_threads = new Dictionary<int, int>();
                using (HttpClient a_client = new HttpClient())
                using (HttpResponseMessage a_response = await a_client.GetAsync("https://8ch.net/" + board + "/catalog.html"))
                using (HttpContent a_content = a_response.Content)
                {
                    string soykaf = await a_content.ReadAsStringAsync();
                    string pattern = "data-reply=\"";
                    for (int i = 0; i < soykaf.Length - pattern.Length; ++i)
                    {
                        if (soykaf.Substring(i, pattern.Length) == pattern)
                        {
                            int replyCountEnd = FindNext(soykaf.Substring(i + pattern.Length), "\"");
                            string replyCount = soykaf.Substring(i + pattern.Length, replyCountEnd);
                            int threadIdBegin = i + pattern.Length + FindNext(soykaf.Substring(i + pattern.Length), "data-id=\"");
                            string threadId = soykaf.Substring(threadIdBegin + 9, FindNext(soykaf.Substring(threadIdBegin + 9), "\""));
                            int threadNameBegin = threadIdBegin + 9 + FindNext(soykaf.Substring(threadIdBegin + 9), "data-subject=\"");
                            string threadName = soykaf.Substring(threadNameBegin + 14, FindNext(soykaf.Substring(threadNameBegin + 14), "\""));

                            if (FindNext(threadName.ToLower(), "r/a/dio") >= 0 || FindNext(threadName.ToLower(), "radio") >= 0)
                            {
                                int.TryParse(threadId, out int ID);
                                int.TryParse(replyCount, out int REPLY);
                                a_threads.Add(ID, REPLY);
                            }
                        }
                    }
                }
                Console.Write("got " + a_threads.Count + " r/a/dio thread" + (a_threads.Count > 1 ? "s" : "") + " from 8/" + board + "/");
                if (board == "a")
                {
                    using (HttpClient smug_client = new HttpClient())
                    using (HttpResponseMessage smug_response = await smug_client.GetAsync("https://smuglo.li/a/catalog.html"))
                    using (HttpContent smug_content = smug_response.Content)
                    {
                        string soykaf = await smug_content.ReadAsStringAsync();
                        string pattern = "data-reply=\"";
                        for (int i = 0; i < soykaf.Length - pattern.Length; ++i)
                        {
                            if (soykaf.Substring(i, pattern.Length) == pattern)
                            {
                                int replyCountEnd = FindNext(soykaf.Substring(i + pattern.Length), "\"");
                                string replyCount = soykaf.Substring(i + pattern.Length, replyCountEnd);
                                int threadIdBegin = i + pattern.Length + FindNext(soykaf.Substring(i + pattern.Length), "data-id=\"");
                                string threadId = soykaf.Substring(threadIdBegin + 9, FindNext(soykaf.Substring(threadIdBegin + 9), "\""));
                                int threadNameBegin = threadIdBegin + 9 + FindNext(soykaf.Substring(threadIdBegin + 9), "data-subject=\"");
                                string threadName = soykaf.Substring(threadNameBegin + 14, FindNext(soykaf.Substring(threadNameBegin + 14), "\""));

                                if (FindNext(threadName.ToLower(), "r/a/dio") >= 0 || FindNext(threadName.ToLower(), "radio") >= 0)
                                {
                                   if (int.TryParse(threadId, out int ID) && int.TryParse(replyCount, out int REPLY))
                                   {
                                       smug_threads.Add(ID, REPLY);
                                   }
                                }
                            }
                        }
                    }
                    Console.Write("\ngot " + smug_threads.Count + " r/a/dio thread" + (smug_threads.Count > 1 ? "s" : "") + " from the bunker");
                }
                Thread.Sleep(500);
                Console.Write("\nopening the most active thread(s)");
                Thread.Sleep(1000);
                foreach (var x in a_threads)
                {
                    Process.Start("https://8ch.net/a/res/" + x.Key + ".html");
                    break;
                }
                foreach (var x in smug_threads)
                {
                    Process.Start("https://smuglo.li/a/res/" + x.Key + ".html");
                    break;
                }
                return;
            }
            if (cmd.StartsWith("play"))
            {
                if (M3uCheck())
                    return;

                wasapiOut.Play();
                Console.Write("started playing");
                return;
            }
            if (cmd.StartsWith("stop"))
            {
                if (M3uCheck())
                    return;

                wasapiOut.Stop();
                Console.Write("stopped playing");
                return;
            }
            if (cmd.StartsWith("pause"))
            {
                if (M3uCheck())
                    return;

                wasapiOut.Pause();
                Console.Write("paused playing");
                return;
            }
            if (cmd.StartsWith("resume"))
            {
                if (M3uCheck())
                    return;

                wasapiOut.Resume();
                Console.Write("resumed playing");
                return;
            }
            if (cmd.StartsWith("help"))
            {
                Console.Write(HELP_MESSAGE);
                return;
            }

            Console.ForegroundColor = ConsoleColor.Black;
            Console.BackgroundColor = ConsoleColor.Red;
            Console.Write("nANI!?");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.BackgroundColor = ConsoleColor.Black;
            Console.Write("?");
            return;
        }
        private void Prefix()
        {
            Console.ForegroundColor = ConsoleColor.Black;
            Console.BackgroundColor = ConsoleColor.White;
            Console.Write("\n/a/non");
            Console.ForegroundColor = ConsoleColor.White;
            Console.BackgroundColor = ConsoleColor.Black;
            Console.Write("> ");
        }
        public async void Run()
        {
            MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
            enumerator.DefaultDeviceChanged += (a, b) =>
            {
                if (!initialized)
                    return;

                initialized = false;
                wasapiOut.Stop();
                wasapiOut = new WasapiOut();
            };

            Console.ForegroundColor = ConsoleColor.Black;
            Console.BackgroundColor = ConsoleColor.Gray;
            string msg = "r/a/dio terminal, version 0";
            Console.Write(msg + Repeat(" ", Console.BufferWidth - msg.Length - 1));
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.BackgroundColor = ConsoleColor.Black;
            Console.Write(INTRO_LOGO);
            Console.ForegroundColor = ConsoleColor.White;

            new Thread(new ThreadStart(Music)).Start();
            Prefix();
            Console.Write("playlist default.m3u\n");
            await Cmd("playlist default.m3u");
            Prefix();
            Console.Write("vol 0,2\n");
            await Cmd("vol 0,2");
            Prefix();
            Console.Write("play\n");
            await Cmd("play");
            await Task.Run(async () =>
            {
                while (true)
                {
                    Prefix();
                    await Cmd(Console.ReadLine());
                }
            });
        }
    }
}
