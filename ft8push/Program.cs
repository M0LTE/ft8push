using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Linq;
using PushoverClient;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ft8push
{
    class Program
    {
        const string myPushoverUserKey = "u7rg1pn71hcnnqiuiit32dzxsmqief";
        const string thisAppKey = "aykg6qr18k7bvpcf2mzr3bnpd29hp5";
        const string myLocator = "IO91";

        static List<Spot> spots = new List<Spot>();
        static Stopwatch quietTimer = new Stopwatch();

        static void Main(string[] args)
        {
            Task.Factory.StartNew(SpotPusher, TaskCreationOptions.LongRunning);

            using (var client = new UdpClient(2237, AddressFamily.InterNetwork))
            {
                while (true)
                {
                    var ipep = new IPEndPoint(IPAddress.Loopback, 0);

                    byte[] msg = client.Receive(ref ipep);

                    if (msg.Length < 55)
                        continue;

                    if (msg[11] == 0x02)
                    {
                        string text;
                        try
                        {
                            text = Encoding.ASCII.GetString(msg.Skip(52).SkipLast(2).ToArray());
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        string[] split = text.Split(' ');

                        if (split.Length != 3)
                        {
                            continue;
                        }

                        string heardCall = split[1];
                        string heardLoc;

                        if (IsLocator(split[2]) && split[2] != "RR73")
                        {
                            heardLoc = split[2];
                        }
                        else
                        {
                            heardLoc = null;
                        }

                        if (heardLoc != null)
                        {
                            double distkm;
                            try
                            {
                                distkm = MaidenheadLocator.Distance(myLocator, heardLoc);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex);
                                continue;
                            }

                            var spot = new Spot { TheirCall = heardCall, TheirLocator = heardLoc, Distance = distkm };

                            lock (spots)
                            {
                                if (!spots.Any(s => s.TheirCall == heardCall))
                                {
                                    spots.Add(spot);
                                }
                            }

                            quietTimer.Restart();
                        }
                    }
                }
            }
        }

        static void SpotPusher()
        {
            while (true)
            {
                Thread.Sleep(100);

                if (quietTimer.Elapsed > TimeSpan.FromSeconds(5))
                {
                    lock (spots)
                    {
                        if (spots.Count > 0)
                        {
                            string msg = $"Heard {String.Join(", ", spots.OrderByDescending(s=>s.Distance))} at {(spots[0].Timestamp):HH:mm}";

                            Console.WriteLine(msg);

                            Push(msg);

                            spots.Clear();
                        }
                    }
                }
            }
        }

        static void Push(string msg)
        {
            Pushover pclient = new Pushover(appKey: thisAppKey);
            PushResponse response = pclient.Push(title: "FT-8 Spot", message: msg, userKey: myPushoverUserKey);
        }

        static bool IsLocator(string v)
        {
            if (string.IsNullOrWhiteSpace(v))
                return false;

            if (v.Length != 4)
                return false;

            return char.IsLetter(v[0]) && char.IsLetter(v[1]) && char.IsNumber(v[2]) && char.IsNumber(v[3]);
        }
    }
}
