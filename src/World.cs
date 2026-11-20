using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace CovertCrew
{
    // Everything shared by the crew: the clock, the screen, settings, reactions, tray icon.
    static class World
    {
        public static readonly string[] Ids = { "dad", "mom", "kid", "dog" };
        public static readonly Dictionary<string, double> BaseHeight = new Dictionary<string, double> { { "dad", 100 }, { "mom", 90 }, { "kid", 60 }, { "dog", 40 } };
        public static readonly Dictionary<string, string> DefaultNames = new Dictionary<string, string> { { "dad", "Agent Victor" }, { "mom", "Goldie" }, { "kid", "Pip" }, { "dog", "Biscuit" } };
        public static readonly Dictionary<string, string> ArtSvg = new Dictionary<string, string>();
        public static readonly Dictionary<string, Member> Members = new Dictionary<string, Member>();
        public static Settings Settings;

        public static double Now;
        public static Rect WA;
        public static double Floor;
        public static Point Cursor;
        public static double LastCursorMove;
        public static double IdleSeconds;
        public static bool Dozing;
        static bool hiddenForFullscreen, userHidden;

        static readonly Random rng = new Random();
        static readonly Stopwatch clock = Stopwatch.StartNew();
        static double lastFrame, lastHousekeep, lastWork, lastChase, lastSample;
        static readonly Queue<bool> activeSamples = new Queue<bool>();
        static string lastQuery = "", lastTitle = "";
        static WinForms.NotifyIcon tray;

        // ----- helpers -----
        public static double Rand(double a, double b) { return a + rng.NextDouble() * (b - a); }
        public static bool Chance(double p) { return rng.NextDouble() < p; }
        public static T Pick<T>(IList<T> list) { return list[rng.Next(list.Count)]; }
        public static double Clamp(double v, double a, double b) { return Math.Max(a, Math.Min(b, v)); }

        public static string Weighted(Dictionary<string, double> table)
        {
            double total = table.Values.Sum();
            double r = rng.NextDouble() * total;
            foreach (var kv in table) if ((r -= kv.Value) <= 0) return kv.Key;
            return table.Keys.First();
        }

        public static void Later(double ms, Action fn)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)) };
            t.Tick += delegate { t.Stop(); fn(); };
            t.Start();
        }

        static void Each(Action<Member> fn) { foreach (var m in Members.Values.ToList()) fn(m); }

        static void Who(string id, double delay, Action<Member> fn)
        {
            Later(delay, delegate
            {
                Member m;
                if (Members.TryGetValue(id, out m) && !m.Dead) fn(m);
            });
        }

        // ----- startup -----
        [STAThread]
        static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "CovertCrewDesktopPet", out created))
            {
                if (!created) return; // already running
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Startup += delegate { Start(); };
                app.Run();
            }
        }

        static void Start()
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var id in Ids)
            {
                using (var s = asm.GetManifestResourceStream(id + ".svg"))
                using (var r = new StreamReader(s)) ArtSvg[id] = r.ReadToEnd();
            }
            Settings = Settings.Load();
            Measure();
            SetupTray();
            ApplyMembers();
            CompositionTarget.Rendering += OnFrame;
            if (Settings.FirstRun)
            {
                Settings.FirstRun = false;
                Settings.Save();
                Later(2500, delegate
                {
                    Who("kid", 0, m => m.Say("Hi! We live here now!", 3000));
                    tray.ShowBalloonTip(5000, "Covert Crew moved in!", "Right-click the hat icon by the clock for options. Drag them around, click to poke, wiggle your mouse over them to pet.", WinForms.ToolTipIcon.Info);
                });
            }
        }

        static void Measure()
        {
            var screen = WinForms.Screen.PrimaryScreen;
            WA = SystemParameters.WorkArea;
            if (WA.Width > 0) Native.Dpi = screen.WorkingArea.Width / WA.Width;
            Floor = WA.Bottom;
        }

        public static void ApplyMembers()
        {
            foreach (var id in Ids)
            {
                bool want = Settings.Members[id] && !userHidden;
                Member m;
                Members.TryGetValue(id, out m);
                if (want && m == null) Members[id] = new Member(id);
                else if (!want && m != null) { m.Destroy(); Members.Remove(id); }
            }
        }

        static void Rebuild()
        {
            foreach (var m in Members.Values) m.BuildLook();
        }

        // ----- main loop -----
        static void OnFrame(object sender, EventArgs e)
        {
            Now = clock.Elapsed.TotalSeconds;
            double dt = Math.Min(0.05, Math.Max(0, Now - lastFrame));
            lastFrame = Now;

            var c = Native.Cursor();
            if (c != Cursor)
            {
                double speed = dt > 0 ? (c - Cursor).Length / dt : 0;
                Cursor = c;
                LastCursorMove = Now;
                MaybeChaseCursor(speed);
            }

            foreach (var m in Members.Values.ToList())
            {
                if (m.Dead) continue;
                m.TrackPetting(Cursor);
                m.Update(dt);
                m.Render();
            }

            if (Now - lastSample > 0.5) SampleActivity();
            if (Now - lastHousekeep > 1) Housekeeping();
        }

        static void MaybeChaseCursor(double speed)
        {
            Member dog;
            if (!Members.TryGetValue("dog", out dog)) return;
            if (speed > 1400 && Cursor.Y > Floor - 140 && Now - lastChase > 15 && dog.IsFree() && dog.Platform == IntPtr.Zero && Chance(0.08))
            {
                lastChase = Now;
                dog.StartAct(new Act("chase") { T = 4 });
            }
        }

        // Count how busy the user is (mouse/keyboard), without reading any input.
        static void SampleActivity()
        {
            lastSample = Now;
            IdleSeconds = Native.IdleMs() / 1000.0;
            if (IdleSeconds < 1 && Dozing) WakeAll();
            activeSamples.Enqueue(IdleSeconds < 0.6);
            while (activeSamples.Count > 20) activeSamples.Dequeue();
            if (Settings.WorkEmotes && activeSamples.Count == 20 && activeSamples.Count(b => b) >= 16 && Now - lastWork > 25)
            {
                lastWork = Now;
                activeSamples.Clear();
                Cheer();
            }
        }

        static void Housekeeping()
        {
            lastHousekeep = Now;
            Measure();
            IntPtr fg = Native.GetForegroundWindow();

            // Step aside for full-screen apps (videos, games, presentations).
            bool full = false;
            if (fg != IntPtr.Zero && !Native.IsOwn(fg) && !Native.IsShell(fg))
            {
                var r = Native.Frame(fg);
                full = r.Left <= 0.5 && r.Top <= 0.5 && r.Right >= SystemParameters.PrimaryScreenWidth - 0.5 && r.Bottom >= SystemParameters.PrimaryScreenHeight - 0.5;
            }
            if (full != hiddenForFullscreen)
            {
                hiddenForFullscreen = full;
                foreach (var m in Members.Values) m.View.Visibility = full ? Visibility.Hidden : Visibility.Visible;
            }
            if (!full) foreach (var m in Members.Values) Native.BringTopmost(m.View.Handle);

            // React to searches by reading the browser's window title.
            if (fg != IntPtr.Zero && !Native.IsOwn(fg))
            {
                string title = Native.Title(fg);
                if (title != lastTitle)
                {
                    lastTitle = title;
                    string q = QueryFromTitle(title);
                    if (q != null) React(q);
                }
            }
        }

        // ----- geometry: standing on windows -----
        public static bool FindLanding(Member m, double y0, double y1, out IntPtr hit, out double top)
        {
            hit = IntPtr.Zero;
            top = 0;
            if (y1 < WA.Top + 8 || y0 > Floor - 12) return false;
            IntPtr h = Native.TopWindowAt(m.X, Math.Min(y1 + 2, Floor - 1));
            if (!Native.IsLandable(h)) return false;
            Rect r = Native.Frame(h);
            if (r.Top >= y0 - 0.5 && r.Top <= y1 + 2 && r.Width >= 80 && r.Top > WA.Top + m.H * 0.9 && r.Top < Floor - 12)
            {
                hit = h;
                top = r.Top;
                return true;
            }
            return false;
        }

        public static bool TopVisibleAt(IntPtr h, double x, double top)
        {
            IntPtr at = Native.TopWindowAt(x, top + 3);
            return at == h || Native.IsOwn(at);
        }

        public static Perch FindPerch(Member m)
        {
            if (!m.Grounded || m.Platform != IntPtr.Zero) return null;
            var found = new List<Perch>();
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!Native.IsLandable(h)) return true;
                Rect r = Native.Frame(h);
                if (r.Width < m.W * 1.5 || r.Height < 40) return true;
                if (r.Top < WA.Top + m.H + 20 || r.Top > Floor - 50 || Floor - r.Top > m.P.Jump) return true;
                double left = Math.Max(r.Left, WA.Left) + m.W * 0.6;
                double right = Math.Min(r.Right, WA.Right) - m.W * 0.6;
                if (right <= left) return true;
                found.Add(new Perch { Hwnd = h, Left = left, Right = right, Top = r.Top });
                return true;
            }, IntPtr.Zero);
            for (int tries = 0; tries < 5 && found.Count > 0; tries++)
            {
                int i = rng.Next(found.Count);
                var c = found[i];
                if (TopVisibleAt(c.Hwnd, (c.Left + c.Right) / 2, c.Top)) return c;
                found.RemoveAt(i);
            }
            return null;
        }

        // ----- pair interactions -----
        static readonly Dictionary<string, string[]> Pairs = new Dictionary<string, string[]>
        {
            { "dad+mom", new[] { "chat", "chat", "shy" } },
            { "dad+kid", new[] { "highfive", "chat", "pat" } },
            { "kid+mom", new[] { "hug", "chat", "highfive" } },
            { "dad+dog", new[] { "pet" } },
            { "dog+mom", new[] { "pet" } },
            { "dog+kid", new[] { "play", "pet" } },
        };

        static readonly Dictionary<string, string[][]> Convos = new Dictionary<string, string[][]>
        {
            { "dad+mom", new[] {
                new[] { "Busy day?", "I fixed three gadgets!", "Impressive.", "Hehe ♥" },
                new[] { "Dinner plans?", "Something not on fire.", "...Deal." },
                new[] { "You look nice today.", "O-oh! Thank you…" },
                new[] { "Any news from HQ?", "You tell me, Agent.", "Classified.", "Hmph!" } } },
            { "dad+kid", new[] {
                new[] { "Homework done?", "...Mostly?", "{kid}." },
                new[] { "Want to learn a spy trick?", "YES!!", "Step one: be quiet.", "…" },
                new[] { "Report, junior agent.", "Everything is awesome!" } } },
            { "kid+mom", new[] {
                new[] { "Mom! Look at this!", "Wow, amazing!" },
                new[] { "Can we build a robot?", "Only a small one.", "YAY!" },
                new[] { "I'm hungry…", "Snacks after this!" } } },
        };

        public static void StartInteraction(Member a, Member b)
        {
            var ids = new[] { a.Id, b.Id };
            Array.Sort(ids, StringComparer.Ordinal);
            string pair = ids[0] + "+" + ids[1];
            string[] kinds;
            string kind = Pairs.TryGetValue(pair, out kinds) ? Pick(kinds) : "chat";
            string[] script = null;
            double t = 2.6;
            if (kind == "chat")
            {
                string[][] convos;
                if (!Convos.TryGetValue(pair, out convos)) convos = new[] { new[] { "Hi!", "Hello!" } };
                string kidName = Settings.NameOf("kid");
                script = Pick(convos).Select(l => l.Replace("{kid}", kidName)).ToArray();
                t = script.Length * 1.6 + 1;
            }
            a.Facing = b.X > a.X ? 1 : -1;
            b.Facing = -a.Facing;
            a.StartAct(new Act("interact") { Kind = kind, Partner = b, Role = 0, T = t, Script = script });
            b.StartAct(new Act("interact") { Kind = kind, Partner = a, Role = 1, T = t });
        }

        public static void StartKind(Member m, Act a)
        {
            bool dog = m.Id == "dog";
            switch (a.Kind)
            {
                case "shy":
                    m.SetState("idle");
                    m.SetExpr("shy", 2600);
                    m.Particles("💕", 2);
                    break;
                case "highfive":
                    m.SetState(dog ? "idle" : "highfive");
                    m.SetExpr("excited", 2000);
                    if (a.Role == 0) { m.Emote("✋", 1400); Later(500, delegate { m.Particles("✨", 3); }); }
                    break;
                case "hug":
                    m.SetState("hug");
                    m.SetExpr("love", 2600);
                    m.Particles("💗", 2);
                    break;
                case "pat":
                    m.SetState(m.Id == "dad" ? "pat" : "idle");
                    if (m.Id == "kid") { m.SetExpr("shy", 2400); m.Emote("😊"); }
                    else m.SetExpr("happy", 2400);
                    break;
                case "pet":
                    m.SetState(dog ? "idle" : "pet");
                    if (dog) { m.SetExpr("love", 2600); m.Particles("💕", 3); }
                    else
                    {
                        m.SetExpr("happy", 2600);
                        if (Chance(0.4)) m.Say(Pick(new[] { "Good boy!", "Who's a good dog?", "Such a good pup." }), 1800);
                    }
                    break;
                case "play":
                    m.SetState(dog ? "spin" : "dance");
                    m.SetExpr("excited", 3000);
                    m.Emote(dog ? "🐾" : "✨");
                    break;
                default:
                    m.SetState("idle");
                    break;
            }
        }

        // ----- commands -----
        public static void Command(string cmd)
        {
            var offs = new Dictionary<string, double> { { "dad", -1.5 }, { "mom", -0.5 }, { "kid", 0.5 }, { "dog", 1.5 } };
            switch (cmd)
            {
                case "gather":
                    Each(m => m.Order(new[] { new Act("walk") { X = WA.Left + WA.Width / 2 + offs[m.Id] * 62 * Settings.Scale, Run = true }, new Act("wave") { T = 2 } }, true));
                    break;
                case "party":
                    Each(m =>
                    {
                        m.Order(new[] { new Act(m.Id == "dog" ? "spin" : "dance") { T = 6 } }, false);
                        Later(Rand(0, 600), delegate { m.Emote(Pick(new[] { "🎉", "🎵", "🎶", "🥳" }), 2000); });
                    });
                    break;
                case "sleep":
                    Each(m => m.Order(new[] { new Act("sleep") { T = 30 } }, true));
                    break;
                case "wave":
                    Each(m => m.Order(new[] { new Act(m.Id == "dog" ? "spin" : "wave") { T = 2 } }, false));
                    Who("kid", 300, m => m.Say("Hiii!", 1600));
                    break;
            }
        }

        static void WakeAll()
        {
            Dozing = false;
            Each(m =>
            {
                if (m.Act != null && (m.Act.Name == "sleep" || m.Act.Name == "nap"))
                {
                    m.SetExpr("surprised", 1200);
                    m.Emote("❗", 1200);
                    m.FinishAct();
                }
            });
        }

        static void Cheer()
        {
            var free = Members.Values.Where(x => (x.Grounded || x.Clinging) && !x.Dead).ToList();
            if (free.Count == 0) return;
            var m = Pick(free);
            m.SetExpr(Pick(new[] { "happy", "excited", "focused" }), 2000);
            if (Chance(0.55)) m.Emote(Pick(m.P.WorkEmotes), 2200);
            else m.Say(Pick(m.P.WorkLines), 2200);
            if (Chance(0.5)) m.Hop();
            var others = free.Where(x => x != m).ToList();
            if (others.Count > 0 && Chance(0.3))
            {
                var o = Pick(others);
                Later(900, delegate { o.Emote(Pick(o.P.WorkEmotes), 2000); });
            }
        }

        // ----- search reactions -----
        static readonly Regex BrowserSuffix = new Regex(@"\s+(?:and \d+ more pages?\s+)?(?:[-—–]\s+(?:Personal|Work|Profile \d+|[^-—–]*profile))?\s*[-—–]\s+(?:Google Chrome|Microsoft​? Edge|Mozilla Firefox|Brave|Opera|Vivaldi|Arc)$", RegexOptions.IgnoreCase);
        static readonly Regex SearchTitle = new Regex(@"^(.+?)\s+[-—–]\s+(?:Google Search|Search|Bing|YouTube|Wikipedia|Yahoo Search Results|Brave Search|Ecosia|Pinterest)$", RegexOptions.IgnoreCase);
        static readonly Regex DdgTitle = new Regex(@"^(.+?) at DuckDuckGo$", RegexOptions.IgnoreCase);

        static string QueryFromTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            string t = BrowserSuffix.Replace(title.Trim(), "").Trim();
            var m = SearchTitle.Match(t);
            if (m.Success) return m.Groups[1].Value;
            m = DdgTitle.Match(t);
            if (m.Success) return m.Groups[1].Value;
            return null;
        }

        class Reaction { public Regex Re; public Action<string> Run; }
        static List<Reaction> reactions;

        static Reaction R(string words, Action<string> run)
        {
            var alts = string.Join("|", words.Split(',').Select(w => Regex.Escape(w.Trim())));
            return new Reaction { Re = new Regex(@"\b(?:" + alts + @")(?:s|es)?\b", RegexOptions.IgnoreCase), Run = run };
        }

        static List<Reaction> BuildReactions()
        {
            return new List<Reaction>
            {
                R("peanut,snack,food,recipe,pizza,cake,cookie,burger,sushi,ice cream,dessert,restaurant,lunch,dinner,breakfast,candy,chocolate,donut,pasta,taco,ramen,bakery,pancake", q =>
                {
                    Each(m => { m.SetExpr(m.Id == "dad" ? "happy" : "love", 3500); m.Hop(); });
                    Who("kid", 0, m => { m.Say(Pick(new[] { "Snack time?!", "I want some!", "Yum yum yum!" })); m.Emote("🤤"); });
                    Who("dog", 200, m => { m.Emote("🍖"); m.SetProp("bone", 6000); });
                    Who("dad", 1600, m => m.Say("Dinner is decided, then.", 2200));
                }),
                R("spy,spies,secret,mission,agent,detective,undercover,espionage,disguise,cipher,heist,mystery,gadget", q =>
                {
                    Who("dad", 0, m => { m.Say("Mission accepted.", 2200); m.Order(new[] { new Act("walk") { Sneak = true, Expr = "smug", X = Rand(m.MinX, m.MaxX) } }, false); });
                    Who("kid", 1200, m => { m.SetExpr("excited", 3000); m.Say("Ooh, a secret mission!"); m.Hop(); });
                    Who("mom", 600, m => m.Emote("🤫"));
                    Who("dog", 900, m => m.Emote("🔍"));
                }),
                R("dog,puppy,puppies,doggo,dachshund,pup,pet,wiener dog", q =>
                {
                    Who("dog", 0, m => { m.SetExpr("excited", 3000); m.Say("Woof woof!"); m.Order(new[] { new Act("spin") { T = 2 } }, false); });
                    Who("kid", 500, m => { m.SetExpr("love", 2500); m.Emote("💕"); });
                }),
                R("cat,kitten,kitty", q =>
                {
                    Who("dog", 0, m => { m.SetExpr("angry", 3000); m.Say("Grrr…"); m.Shake(800); });
                    Who("kid", 700, m => { m.SetExpr("love", 2500); m.Emote("😻"); });
                }),
                R("homework,math,exam,test,quiz,study,school,essay,grades,algebra,calculus,report card,worksheet", q =>
                {
                    Who("kid", 0, m => { m.SetExpr("scared", 3200); m.Emote("😱"); m.Say("N-not homework…"); m.Shake(900); });
                    Who("dad", 1400, m => { m.SetExpr("focused", 2500); m.Say("Study hard, " + Settings.NameOf("kid") + "."); });
                    Who("mom", 900, m => m.Emote("📚"));
                }),
                R("scary,horror,ghost,zombie,monster,haunted,creepy,spider,halloween,nightmare,vampire", q =>
                {
                    Each(m => { m.SetExpr("scared", 3000); m.Shake(700); });
                    Who("kid", 300, m =>
                    {
                        m.Emote("😱");
                        Member mom;
                        if (Members.TryGetValue("mom", out mom) && mom.Grounded && m.Grounded && mom.Platform == m.Platform)
                            m.Order(new[] { new Act("follow") { Who = mom, T = 4, Expr = "scared" } }, false);
                    });
                    Who("dog", 200, m => m.Say("Woof?!", 1500));
                    Who("dad", 1200, m => m.Say("Stay behind me."));
                }),
                R("love,romance,romantic,wedding,date,valentine,couple,anniversary,kiss,crush", q =>
                {
                    Who("dad", 0, m => { m.SetExpr("shy", 3000); m.Particles("💕", 2); });
                    Who("mom", 200, m => { m.SetExpr("shy", 3000); m.Particles("💗", 2); });
                    Who("kid", 900, m => { m.SetExpr("smug", 2500); m.Say("Ooooh~"); });
                    Who("dog", 500, m => m.Emote("💕"));
                }),
                R("workout,gym,fitness,martial,karate,boxing,exercise,yoga,kick,training,marathon,running,sport", q =>
                {
                    Who("mom", 0, m => { m.Say("Let's train!"); m.Order(new[] { new Act("train") { T = 4 } }, false); });
                    Who("kid", 600, m => { m.Emote("💪"); m.Order(new[] { new Act("hops") }, false); });
                    Who("dad", 1000, m => m.Emote("😅"));
                }),
                R("cartoon,anime,movie,tv,game,toy,netflix,video game,minecraft,comic,trailer", q =>
                {
                    Who("kid", 0, m => { m.SetExpr("excited", 3500); m.Say("Ooh! Can we watch?!"); m.Order(new[] { new Act("dance") { T = 4 } }, false); });
                    Who("dog", 700, m => m.Order(new[] { new Act("spin") { T = 1.5 } }, false));
                }),
                R("rain,weather,storm,snow,forecast,umbrella,sunny,hurricane,thunder", q =>
                {
                    string sym = q.Contains("snow") ? "❄️" : q.Contains("sun") ? "☀️" : (q.Contains("storm") || q.Contains("thunder") || q.Contains("hurricane")) ? "⛈️" : "☔";
                    double d = 0;
                    Each(m => { d += 250; Later(d, delegate { m.Emote(sym, 2200); }); });
                }),
                R("sleep,tired,nap,insomnia,bed,bedtime,yawn,pillow", q =>
                {
                    double d = 0;
                    Each(m => { d += 300; Later(d, delegate { m.SetExpr("sleepy", 2500); m.Emote("🥱"); }); });
                    Who("dog", 1500, m => m.Order(new[] { new Act("nap") }, false));
                }),
                R("money,bank,tax,budget,bill,price,rent,mortgage,loan,salary,invoice", q =>
                {
                    Who("dad", 0, m => { m.SetExpr("nervous", 3000); m.Emote("💸"); m.Say("Ah… expenses."); });
                    Who("mom", 900, m => m.Emote("🧾"));
                }),
                R("music,song,dance,party,concert,playlist,karaoke,guitar,piano,dj", q => Command("party")),
                R("flower,garden,gardening,plant,rose,tulip,bouquet", q =>
                {
                    Who("mom", 0, m => { m.SetExpr("love", 3000); m.Emote("🌸"); m.Say("So pretty!"); });
                    Who("dog", 600, m => m.Order(new[] { new Act("sniff") }, false));
                }),
                R("robot,engineering,invention,science,tools,diy,repair,circuit,3d print,arduino,coding,programming,javascript,python", q =>
                {
                    Who("mom", 0, m => { m.SetExpr("excited", 3000); m.Say("Ooh, a project!"); m.Order(new[] { new Act("tinker") { T = 4 } }, false); });
                    Who("kid", 1000, m => m.Emote("🤖"));
                }),
                R("coffee,tea,espresso,latte,cafe,cappuccino", q =>
                {
                    Who("dad", 0, m => { m.SetExpr("content", 3000); m.Emote("☕"); m.Say("Now we're talking."); });
                }),
            };
        }

        static void ReactDefault()
        {
            var list = Members.Values.Where(o => !o.Dead && o.State != "held").ToList();
            if (list.Count == 0) return;
            var m = Pick(list);
            m.SetExpr(Pick(new[] { "focused", "surprised" }), 2200);
            m.Emote(Pick(new[] { "🤔", "🔍", "❓", "👀" }));
            if (Chance(0.5))
            {
                var lines = new Dictionary<string, string[]>
                {
                    { "dad", new[] { "Researching, are we?", "Gathering intel…" } },
                    { "mom", new[] { "Ooh, what's that?", "Interesting!" } },
                    { "kid", new[] { "What's that mean?", "Ooh, can I see?" } },
                    { "dog", new[] { "*head tilt*" } },
                };
                m.Say(Pick(lines[m.Id]), 2200);
            }
        }

        public static void React(string q)
        {
            if (!Settings.ReactToSearch || string.IsNullOrWhiteSpace(q) || Members.Count == 0) return;
            q = q.ToLowerInvariant().Trim();
            if (q.Length > 200) q = q.Substring(0, 200);
            if (q == lastQuery) return;
            lastQuery = q;
            if (reactions == null) reactions = BuildReactions();
            var hit = reactions.FirstOrDefault(r => r.Re.IsMatch(q));
            string query = q;
            Later(500, delegate { if (hit != null) hit.Run(query); else ReactDefault(); });
        }

        // ----- tray icon & menu -----
        static void SetupTray()
        {
            tray = new WinForms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location),
                Text = "Covert Crew",
                Visible = true,
            };
            tray.DoubleClick += delegate { Command("gather"); };
            var menu = new WinForms.ContextMenuStrip();
            menu.Opening += delegate { BuildMenu(menu); };
            BuildMenu(menu);
            tray.ContextMenuStrip = menu;
        }

        static WinForms.ToolStripMenuItem Item(string text, Action onClick, bool check = false)
        {
            var it = new WinForms.ToolStripMenuItem(text) { Checked = check };
            it.Click += delegate { onClick(); };
            return it;
        }

        static void BuildMenu(WinForms.ContextMenuStrip menu)
        {
            menu.Items.Clear();
            menu.Items.Add(Item("📣  Gather everyone", () => Command("gather")));
            menu.Items.Add(Item("🎉  Dance party", () => Command("party")));
            menu.Items.Add(Item("👋  Say hi", () => Command("wave")));
            menu.Items.Add(Item("💤  Nap time", () => Command("sleep")));
            menu.Items.Add(new WinForms.ToolStripSeparator());

            var who = new WinForms.ToolStripMenuItem("Who's home");
            foreach (var id in Ids)
            {
                string mid = id;
                who.DropDownItems.Add(Item(Settings.NameOf(id), () =>
                {
                    Settings.Members[mid] = !Settings.Members[mid];
                    Settings.Save();
                    ApplyMembers();
                }, Settings.Members[id]));
            }
            menu.Items.Add(who);

            var size = new WinForms.ToolStripMenuItem("Size");
            foreach (var opt in new[] { Tuple.Create("Small", 0.8), Tuple.Create("Medium", 1.0), Tuple.Create("Large", 1.25), Tuple.Create("Huge", 1.5) })
            {
                double v = opt.Item2;
                size.DropDownItems.Add(Item(opt.Item1, () => { Settings.Scale = v; Settings.Save(); Rebuild(); }, Math.Abs(Settings.Scale - v) < 0.01));
            }
            menu.Items.Add(size);

            var look = new WinForms.ToolStripMenuItem("Names && pictures");
            foreach (var id in Ids)
            {
                string mid = id;
                var sub = new WinForms.ToolStripMenuItem(Settings.NameOf(id));
                sub.DropDownItems.Add(Item("Rename…", () => Rename(mid)));
                sub.DropDownItems.Add(Item("Use my own picture…", () => ChoosePicture(mid)));
                if (File.Exists(Settings.SkinFile(mid)))
                    sub.DropDownItems.Add(Item("Back to the original look", () => { File.Delete(Settings.SkinFile(mid)); Rebuild(); }));
                look.DropDownItems.Add(sub);
            }
            menu.Items.Add(look);

            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(Item("React to my searches", () => { Settings.ReactToSearch = !Settings.ReactToSearch; Settings.Save(); }, Settings.ReactToSearch));
            menu.Items.Add(Item("Cheer me on while I work", () => { Settings.WorkEmotes = !Settings.WorkEmotes; Settings.Save(); }, Settings.WorkEmotes));
            menu.Items.Add(Item("Climb onto my windows", () => { Settings.Roam = !Settings.Roam; Settings.Save(); }, Settings.Roam));
            menu.Items.Add(Item("Start with Windows", () => { Settings.SetAutostart(!Settings.GetAutostart()); }, Settings.GetAutostart()));
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(Item(userHidden ? "Show the crew" : "Hide the crew", () => { userHidden = !userHidden; ApplyMembers(); }));
            menu.Items.Add(Item("Quit", Quit));
        }

        static void ChoosePicture(string id)
        {
            using (var dlg = new WinForms.OpenFileDialog { Title = "Pick a picture for " + Settings.NameOf(id), Filter = "Images|*.png;*.gif;*.jpg;*.jpeg;*.bmp" })
            {
                if (dlg.ShowDialog() != WinForms.DialogResult.OK) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Settings.SkinFile(id)));
                    File.Copy(dlg.FileName, Settings.SkinFile(id), true);
                    Rebuild();
                }
                catch (Exception ex)
                {
                    WinForms.MessageBox.Show("Couldn't use that picture: " + ex.Message, "Covert Crew");
                }
            }
        }

        static void Rename(string id)
        {
            var box = new System.Windows.Controls.TextBox { Text = Settings.NameOf(id), Margin = new Thickness(0, 6, 0, 10), Padding = new Thickness(4), MaxLength = 24 };
            var ok = new System.Windows.Controls.Button { Content = "Save", IsDefault = true, Width = 80, HorizontalAlignment = HorizontalAlignment.Right };
            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "New name for " + DefaultNames[id] + ":" });
            panel.Children.Add(box);
            panel.Children.Add(ok);
            var win = new Window { Title = "Rename", Content = panel, SizeToContent = SizeToContent.WidthAndHeight, MinWidth = 280, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true };
            ok.Click += delegate { win.DialogResult = true; };
            win.Loaded += delegate { box.Focus(); box.SelectAll(); };
            if (win.ShowDialog() == true)
            {
                string v = box.Text.Trim();
                if (v.Length == 0 || v == DefaultNames[id]) Settings.Names.Remove(id);
                else Settings.Names[id] = v;
                Settings.Save();
                Rebuild();
            }
        }

        static void Quit()
        {
            CompositionTarget.Rendering -= OnFrame;
            tray.Visible = false;
            tray.Dispose();
            foreach (var m in Members.Values) m.Destroy();
            Members.Clear();
            Application.Current.Shutdown();
        }
    }

    // Settings live in %APPDATA%\CovertCrew\settings.txt (simple key=value lines).
    class Settings
    {
        public bool ReactToSearch = true, WorkEmotes = true, Roam = true, FirstRun = true;
        public double Scale = 1;
        public readonly Dictionary<string, bool> Members = new Dictionary<string, bool> { { "dad", true }, { "mom", true }, { "kid", true }, { "dog", true } };
        public readonly Dictionary<string, string> Names = new Dictionary<string, string>();

        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CovertCrew");
        static string FilePath { get { return Path.Combine(Dir, "settings.txt"); } }
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public string NameOf(string id)
        {
            string n;
            return Names.TryGetValue(id, out n) ? n : World.DefaultNames[id];
        }

        public string SkinFile(string id) { return Path.Combine(Dir, "pictures", id + ".img"); }
        public string SkinPath(string id) { var p = SkinFile(id); return File.Exists(p) ? p : null; }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                s.FirstRun = false;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "reactToSearch": s.ReactToSearch = v == "1"; break;
                        case "workEmotes": s.WorkEmotes = v == "1"; break;
                        case "roam": s.Roam = v == "1"; break;
                        case "scale":
                            double d;
                            if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) s.Scale = Math.Max(0.6, Math.Min(1.6, d));
                            break;
                        default:
                            if (k.StartsWith("member.") && s.Members.ContainsKey(k.Substring(7))) s.Members[k.Substring(7)] = v == "1";
                            else if (k.StartsWith("name.") && s.Members.ContainsKey(k.Substring(5)) && v.Length > 0) s.Names[k.Substring(5)] = v;
                            break;
                    }
                }
            }
            catch (Exception) { /* fall back to defaults */ }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var lines = new List<string>
                {
                    "reactToSearch=" + (ReactToSearch ? 1 : 0),
                    "workEmotes=" + (WorkEmotes ? 1 : 0),
                    "roam=" + (Roam ? 1 : 0),
                    "scale=" + Scale.ToString(System.Globalization.CultureInfo.InvariantCulture),
                };
                foreach (var kv in Members) lines.Add("member." + kv.Key + "=" + (kv.Value ? 1 : 0));
                foreach (var kv in Names) lines.Add("name." + kv.Key + "=" + kv.Value.Replace("\n", " "));
                File.WriteAllLines(FilePath, lines);
            }
            catch (Exception) { /* ignore */ }
        }

        public bool GetAutostart()
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue("CovertCrew") != null;
        }

        public void SetAutostart(bool on)
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) return;
                if (on) k.SetValue("CovertCrew", "\"" + Assembly.GetExecutingAssembly().Location + "\"");
                else k.DeleteValue("CovertCrew", false);
            }
        }
    }
}
