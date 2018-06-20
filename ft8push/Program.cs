using Dapper;
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
using System.Xml.Serialization;
using System.IO;
using Newtonsoft.Json;
using System.Data.SQLite;
using Telegram.Bot.Types;
using Telegram.Bot;

namespace ft8push
{
    class Program
    {
        class Match
        {
            public override string ToString()
            {
                return $"{CountryName} ({PrimaryPrefix})";
            }

            [JsonProperty("ctry")]
            public string CountryName { get; set; }

            [JsonProperty("pre")]
            public string PrimaryPrefix { get; set; }

            [JsonProperty("tz")]
            public double UtcOffset { get; internal set; }

            [JsonProperty("lon")]
            public double Longitude { get; internal set; }

            [JsonProperty("lat")]
            public double Latitude { get; internal set; }

            [JsonProperty("cnt")]
            public string Continent { get; internal set; }

            [JsonProperty("itu")]
            public int ItuZone { get; internal set; }

            [JsonProperty("cq")]
            public int CqZone { get; internal set; }

            [JsonProperty("nb")]
            public Flags Flags { get; set; }
        }

        public class Flags
        {
            [JsonProperty("xact")]
            public bool? TreatAsExact { get; internal set; }

            [JsonProperty("ituo")]
            public bool? ItuZoneOverride { get; internal set; }

            [JsonProperty("cqo")]
            public bool? CqZoneOverride { get; internal set; }

            [JsonProperty("poso")]
            public bool? LatlonOverride { get; internal set; }

            [JsonProperty("tzo")]
            public bool? UtcOffsetOverride { get; internal set; }

            [JsonProperty("conto")]
            public bool? ContinentOverride { get; internal set; }
        }

        static List<Match> GetMatches(string call)
        {
            for (int i = call.Length; i > 0; i--)
            {
                string key = call.Substring(0, i);
                if (dict.TryGetValue(key, out List<Match> values))
                {
                    return values;
                }
            }

            return new List<Match>();
        }

        const string myPushoverUserKey = "u7rg1pn71hcnnqiuiit32dzxsmqief";
        const string thisAppKey = "aykg6qr18k7bvpcf2mzr3bnpd29hp5";
        const string myLocator = "IO91LK";

        static List<Spot> spots = new List<Spot>();
        static Stopwatch quietTimer = new Stopwatch();

        static Dictionary<string, List<Match>> dict = JsonConvert.DeserializeObject<Dictionary<string, List<Match>>>(System.IO.File.ReadAllText("dict.json"));

        static void N1mmListener()
        {
            while (true)
            {
                var listener = new UdpClient(new IPEndPoint(IPAddress.Any, 12060));

                while (true)
                {
                    IPEndPoint receivedFrom = new IPEndPoint(IPAddress.Any, 0);
                    byte[] msg = listener.Receive(ref receivedFrom);

                    try
                    {
                        ProcessDatagram(msg);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Uncaught exception in {nameof(N1mmListener)}: {ex}");
                    }
                }
            }
        }

        static void ProcessDatagram(byte[] msg)
        {
            if (N1mmXmlContactReplace.TryParse(msg, out N1mmXmlContactReplace cr))
            {
                ProcessContactReplace(cr);
            }
            else if (N1mmXmlContactInfo.TryParse(msg, out N1mmXmlContactInfo ci))
            {
                ProcessContactAdd(ci);
            }
        }

        private static void ProcessContactReplace(N1mmXmlContactReplace cr)
        {
            int band = (int)cr.Band;
            string call = cr.Call;

            AddLogEntryToWorkedBandSquares(call, band);
        }

        static void ProcessContactAdd(N1mmXmlContactInfo ci)
        {
            int band = (int)ci.Band;
            string call = ci.Call;

            AddLogEntryToWorkedBandSquares(call, band);
        }

        static void AddLogEntryToWorkedBandSquares(string call, int band)
        {
            var matches = GetMatches(call);

            if (matches.Count == 0)
            {
#warning Check log
                return;
            }
            else if (matches.Count > 1)
            {
#warning Do something
                return;
            }

            if (band == 70)
            {
#warning 70cm n1mm weird
                return;
            }

            var m = matches.Single();

            if (!BandSquares.Any(mem => mem.Band == band && mem.Prefix == m.PrimaryPrefix))
            {
                Console.WriteLine($"Adding band square {m.CountryName} ({m.PrimaryPrefix}) / {band}MHz");
                BandSquares.Add(new BandSquare { Band = band, Country = m.CountryName, Prefix = m.PrimaryPrefix });
            }
        }

        static void Main(string[] args)
        {
            Task.Factory.StartNew(SpotPusher, TaskCreationOptions.LongRunning);

            Task.Factory.StartNew(N1mmListener, TaskCreationOptions.LongRunning);

            using (var client = new UdpClient(2237, AddressFamily.InterNetwork))
            {
                //int lastSlotSec = -1;
                DateTime lastQuantisedNow = DateTime.MinValue;

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

                        const int tolerateSecsLate = 2;
                        int slotSec;

                        var utcNow = DateTime.UtcNow;
                        if (utcNow.Second > 7.5 + 0 * 15 && utcNow.Second <= 1* 15 + tolerateSecsLate)
                        {
                            slotSec = 0;
                        }
                        else if (utcNow.Second > 7.5 + 1 * 15 && utcNow.Second <= 2 * 15 + tolerateSecsLate)
                        {
                            slotSec = 15;
                        }
                        else if (utcNow.Second > 7.5 + 2 * 15 && utcNow.Second <= 3 * 15 + tolerateSecsLate)
                        {
                            slotSec = 30;
                        }
                        else
                        {
                            slotSec = 45;
                        }

                        var quantisedNow = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, utcNow.Hour, utcNow.Minute, utcNow.Second);
                        while (quantisedNow.Second != slotSec)
                        {
                            quantisedNow = quantisedNow.Subtract(TimeSpan.FromSeconds(1));
                        }

#warning Detect this from WSJT-X at runtime
                        int band = 14;

                        var matches = GetMatches(heardCall);
                        
                        if (matches.Count == 1)
                        {
                            if (!BandSquares.Any(b => b.Band == band && b.Prefix == matches.Single().PrimaryPrefix))
                            {
                                if (quantisedNow != lastQuantisedNow)
                                {
                                    lastQuantisedNow = quantisedNow;
                                    Console.WriteLine($"---{quantisedNow:HH:mm:ss}---");
                                }

                                string mtch = String.Join(" or ", matches.Select(m => $"{m.CountryName} ({m.PrimaryPrefix})"));

                                SendTelegram($"{heardCall} - {mtch}");

                                Console.ForegroundColor = ConsoleColor.White;
                                Console.Write($"{heardCall}");
                                Console.SetCursorPosition(20, Console.CursorTop);
                                Console.Write(mtch);
                                //Console.SetCursorPosition(40, Console.CursorTop);
                                //Console.WriteLine("        <------ new slot!");
                                Console.WriteLine();
                                
                                Console.ForegroundColor = ConsoleColor.Gray;
                            }
                            else
                            {
                                /*
                                Console.ForegroundColor = ConsoleColor.DarkCyan;
                                Console.WriteLine(matches[0].CountryName);
                                Console.ForegroundColor = ConsoleColor.Gray;
                                */
                            }
                        }
                        else if (matches.Count > 1)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.Write($"{heardCall}");
                            Console.SetCursorPosition(20, Console.CursorTop);
                            Console.Write("ambiguous: ");
                            Console.WriteLine(String.Join(", ", matches.Select(m => m.CountryName)));
                            Console.ForegroundColor = ConsoleColor.Gray;
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.Write($"{heardCall}");
                            Console.SetCursorPosition(20, Console.CursorTop);
                            Console.WriteLine("unknown");
                            Console.ForegroundColor = ConsoleColor.Gray;
                        }
                        

                        /*string heardLoc;

                        if (CallLocatorCache.TryGetValue(heardCall, out string loc)) // from cache
                        {
                            heardLoc = loc;
                        }
                        else if (LookupCallLocator(heardCall, out loc)) // from hamqth.com, from address
                        {
                            heardLoc = loc;
                            CallLocatorCache.Add(heardCall, heardLoc); // add to cache
                        }
                        else // rely on the low res version in the 
                        {
                            if (IsLocator(split[2]) && split[2] != "RR73")
                            {
                                heardLoc = split[2];
                            }
                            else
                            {
                                heardLoc = null;
                            }
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
                        }*/
                    }
                }
            }
        }

        static void SendTelegram(string msg)
        {
            try
            {
                var botClient = new TelegramBotClient("539647442:AAE0ELQJq4dB93NNoxu1UhnId9fCLOXmkxY");
                var result = botClient.SendTextMessageAsync(new ChatId(8525092), msg).Result;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.GetBaseException().Message);
            }
        }

        const int contestnr = 0;

        static void RefreshDB()
        {
            string sql = $"select ts Timestamp, Call, Band, Mode from DXLOG where contestnr = {contestnr}";

            var csb = new SQLiteConnectionStringBuilder();

            string dir = @"C:\Users\tomandels\Nextcloud\Radio\N1MM Logger+\Databases";
            string fn = "m0lte.s3db";

            csb.DataSource = Path.Combine(dir, fn);
            
            csb.ReadOnly = true;

            List<LogEntry> dbResults;
            using (var conn = new SQLiteConnection(csb.ToString()))
            {
                conn.Open();
                dbResults = conn.Query<LogEntry>(sql).ToList();
            }

            foreach (LogEntry dbrow in dbResults)
            {
                var matches = GetMatches(dbrow.Call);
                if (matches.Count != 1)
                {
                    continue;
                }

                var m = matches.Single();

                if (!BandSquares.Any(mem => mem.Band == dbrow.Band && mem.Prefix == m.PrimaryPrefix))
                {
                    BandSquares.Add(new BandSquare { Band = dbrow.Band, Country = m.CountryName, Prefix = m.PrimaryPrefix });
                }
            }
        }

        static List<BandSquare> BandSquares = new List<BandSquare>();

        class BandSquare
        {
            public int Band { get; set; }
            public string Country { get; set; }
            public string Prefix { get; set; }
        }

        class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public string Call { get; set; }
            public int Band { get; set; }
            public string Mode { get; set; }
        }

        static Dictionary<string, DateTime> lastHeard = new Dictionary<string, DateTime>();

        static string hamQthSessionKey = null;

        private static bool LookupCallLocator(string lookupCall, out string locator)
        {
            if (hamQthSessionKey == null)
            {
                hamQthSessionKey = GetHamQthSessionKey();
            }

            if (hamQthSessionKey == null)
            {
                locator = null;
                return false;
            }

            if (!DoHamQthLookup(lookupCall, out locator))
            {
                return false;
            }

            return true;
        }

        static bool DoHamQthLookup(string lookupCall, out string locator)
        {
            string url = $"https://www.hamqth.com/xml.php?id={hamQthSessionKey}&callsign={lookupCall}&prg=ft8push";

            var obj = GetObject<HamQTHResult>(url);

            if (obj != null)
            {
                if (obj.Session != null)
                {
                    if (obj.Session.Error == "Session does not exist or expired")
                    {
                        hamQthSessionKey = GetHamQthSessionKey();
                        return DoHamQthLookup(lookupCall, out locator);
                    }
                    else if (obj.Session.Error == "Callsign not found")
                    {
                        locator = null;
                        return false;
                    }
                }

                if (obj.Search != null)
                {
                    if (!String.IsNullOrWhiteSpace(obj.Search.Grid))
                    {
                        locator = obj.Search.Grid.Trim();
                        return true;
                    }
                }
            }

            locator = null;
            return false;
        }

        const string hamqthuser = "m0lte";
        const string hamqthpass = "yqmsn1ukku";

        static T GetObject<T>(string url)
        {
            try
            {
                string xml = new WebClient().DownloadString(url);

                T obj;

                using (var sr = new StringReader(xml))
                {
                    obj = (T)new XmlSerializer(typeof(T)).Deserialize(sr);
                }

                return obj;
            }
            catch (Exception)
            {
                return default(T);
            }
        }

        static Dictionary<string, string> CallLocatorCache = new Dictionary<string, string>();

        static string GetHamQthSessionKey()
        {
            var obj = GetObject<HamQTHResult>($"https://www.hamqth.com/xml.php?u={hamqthuser}&p={hamqthpass}");

            if (obj == null || obj.Session == null || !String.IsNullOrWhiteSpace(obj.Session.Error))
            {
                return null;
            }

            return obj.Session.Session_id;
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
                        List<Spot> filtered = new List<Spot>();

                        // filter spots
                        foreach (var spot in spots)
                        {
                            if (!lastHeard.ContainsKey(spot.TheirCall))
                            {
                                filtered.Add(spot);
                                lastHeard.Add(spot.TheirCall, spot.Timestamp);
                            }
                            else
                            {
                                TimeSpan sinceHeard = DateTime.Now - lastHeard[spot.TheirCall];

                                if (sinceHeard > TimeSpan.FromHours(1))
                                {
                                    filtered.Add(spot);
                                }
                            }
                        }

                        if (filtered.Count > 0)
                        {
                            string msg = $"Heard {String.Join(", ", filtered.OrderByDescending(s => s.Distance))} at {(filtered[0].Timestamp):HH:mm}";

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


    [XmlRoot(ElementName = "session", Namespace = "https://www.hamqth.com")]
    public class HamQTHSession
    {
        [XmlElement(ElementName = "session_id", Namespace = "https://www.hamqth.com")]
        public string Session_id { get; set; }

        [XmlElement(ElementName = "error", Namespace = "https://www.hamqth.com")]
        public string Error { get; set; }
    }

    [XmlRoot(ElementName = "HamQTH", Namespace = "https://www.hamqth.com")]
    public class HamQTHResult
    {
        [XmlElement(ElementName = "session", Namespace = "https://www.hamqth.com")]
        public HamQTHSession Session { get; set; }

        [XmlElement(ElementName = "search", Namespace = "https://www.hamqth.com")]
        public HamQTHSearch Search { get; set; }

        [XmlAttribute(AttributeName = "version")]
        public string Version { get; set; }

        [XmlAttribute(AttributeName = "xmlns")]
        public string Xmlns { get; set; }
    }

    [XmlRoot(ElementName = "search", Namespace = "https://www.hamqth.com")]
    public class HamQTHSearch
    {
        [XmlElement(ElementName = "callsign", Namespace = "https://www.hamqth.com")]
        public string Callsign { get; set; }
        [XmlElement(ElementName = "nick", Namespace = "https://www.hamqth.com")]
        public string Nick { get; set; }
        [XmlElement(ElementName = "qth", Namespace = "https://www.hamqth.com")]
        public string Qth { get; set; }
        [XmlElement(ElementName = "country", Namespace = "https://www.hamqth.com")]
        public string Country { get; set; }
        [XmlElement(ElementName = "adif", Namespace = "https://www.hamqth.com")]
        public string Adif { get; set; }
        [XmlElement(ElementName = "itu", Namespace = "https://www.hamqth.com")]
        public string Itu { get; set; }
        [XmlElement(ElementName = "cq", Namespace = "https://www.hamqth.com")]
        public string Cq { get; set; }
        [XmlElement(ElementName = "grid", Namespace = "https://www.hamqth.com")]
        public string Grid { get; set; }
        [XmlElement(ElementName = "adr_name", Namespace = "https://www.hamqth.com")]
        public string Adr_name { get; set; }
        [XmlElement(ElementName = "adr_street1", Namespace = "https://www.hamqth.com")]
        public string Adr_street1 { get; set; }
        [XmlElement(ElementName = "adr_city", Namespace = "https://www.hamqth.com")]
        public string Adr_city { get; set; }
        [XmlElement(ElementName = "adr_zip", Namespace = "https://www.hamqth.com")]
        public string Adr_zip { get; set; }
        [XmlElement(ElementName = "adr_country", Namespace = "https://www.hamqth.com")]
        public string Adr_country { get; set; }
        [XmlElement(ElementName = "adr_adif", Namespace = "https://www.hamqth.com")]
        public string Adr_adif { get; set; }
        [XmlElement(ElementName = "district", Namespace = "https://www.hamqth.com")]
        public string District { get; set; }
        [XmlElement(ElementName = "lotw", Namespace = "https://www.hamqth.com")]
        public string Lotw { get; set; }
        [XmlElement(ElementName = "qsl", Namespace = "https://www.hamqth.com")]
        public string Qsl { get; set; }
        [XmlElement(ElementName = "qsldirect", Namespace = "https://www.hamqth.com")]
        public string Qsldirect { get; set; }
        [XmlElement(ElementName = "eqsl", Namespace = "https://www.hamqth.com")]
        public string Eqsl { get; set; }
        [XmlElement(ElementName = "email", Namespace = "https://www.hamqth.com")]
        public string Email { get; set; }
        [XmlElement(ElementName = "jabber", Namespace = "https://www.hamqth.com")]
        public string Jabber { get; set; }
        [XmlElement(ElementName = "skype", Namespace = "https://www.hamqth.com")]
        public string Skype { get; set; }
        [XmlElement(ElementName = "birth_year", Namespace = "https://www.hamqth.com")]
        public string Birth_year { get; set; }
        [XmlElement(ElementName = "lic_year", Namespace = "https://www.hamqth.com")]
        public string Lic_year { get; set; }
        [XmlElement(ElementName = "web", Namespace = "https://www.hamqth.com")]
        public string Web { get; set; }
        [XmlElement(ElementName = "latitude", Namespace = "https://www.hamqth.com")]
        public string Latitude { get; set; }
        [XmlElement(ElementName = "longitude", Namespace = "https://www.hamqth.com")]
        public string Longitude { get; set; }
        [XmlElement(ElementName = "continent", Namespace = "https://www.hamqth.com")]
        public string Continent { get; set; }
        [XmlElement(ElementName = "utc_offset", Namespace = "https://www.hamqth.com")]
        public string Utc_offset { get; set; }
        [XmlElement(ElementName = "picture", Namespace = "https://www.hamqth.com")]
        public string Picture { get; set; }
    }


}