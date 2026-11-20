using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace CovertCrew
{
    class Act
    {
        public string Name;
        public double T = double.NaN, X = double.NaN, Timeout = double.NaN;
        public double El, Speed, TopY, NextHop, NextZ;
        public int Side, Role, Si;
        public bool Run, Sneak, OffEdge, Fail, Locked;
        public string Phase, Expr, Kind, State;
        public Member Who, Partner;
        public string[] Script;
        public Perch Target;
        public Act(string name) { Name = name; }
        public Act Clone() { return (Act)MemberwiseClone(); }
    }

    class Perch { public IntPtr Hwnd; public double Left, Right, Top; }

    class Persona
    {
        public double Speed, ClimbSpeed, Jump;
        public bool Climber;
        public Dictionary<string, double> Acts;
        public string[] Click, ClickEmote, ClimbEmote, PetLines, WorkEmotes, WorkLines;
    }

    class StayDef
    {
        public string State, Expr, Prop, Tick;
        public double TMin = 3, TMax = 6, EmoteChance = 0.7, SayChance = 0.3;
        public string[] Emotes, Says;
    }

    class Expression
    {
        public string E, M;
        public bool B, S, Shades;
        public Expression(string e, string m, bool b = false, bool s = false, bool shades = false) { E = e; M = m; B = b; S = s; Shades = shades; }
    }

    // One family member: their little brain, movement and physics.
    class Member
    {
        public const double G = 2000;

        public static readonly Dictionary<string, Persona> Personas = new Dictionary<string, Persona>
        {
            { "dad", new Persona {
                Speed = 70, ClimbSpeed = 55, Jump = 380, Climber = true,
                Acts = new Dictionary<string, double> { {"idle",3},{"walk",4},{"climb",1.2},{"perch",1.4},{"sit",0.8},{"read",1.5},{"sneak",1},{"social",2.2},{"wave",0.3} },
                Click = new[] { "Hm?", "Need something?", "Stay sharp.", "All quiet on my end.", "Careful, that's classified." },
                ClickEmote = new[] { "❗", "🕶️", "🤨" }, ClimbEmote = new[] { "🧗", "🔭", "😎" },
                PetLines = new[] { "...This is nice.", "Ahem. Thank you." },
                WorkEmotes = new[] { "☕", "👍", "📈", "🕶️" }, WorkLines = new[] { "Good work, agent.", "Efficient.", "Keep it up." } } },
            { "mom", new Persona {
                Speed = 78, ClimbSpeed = 65, Jump = 420, Climber = true,
                Acts = new Dictionary<string, double> { {"idle",2},{"walk",4},{"climb",2},{"perch",1.8},{"train",1.2},{"tinker",1.2},{"social",2.2},{"sit",0.8} },
                Click = new[] { "Oh! Hello!", "Wanna see my newest gadget?", "Hehe, that tickles!", "Need a hand?" },
                ClickEmote = new[] { "😊", "🔧", "✨" }, ClimbEmote = new[] { "💪", "😄", "🌟" },
                PetLines = new[] { "Aww, thank you!", "Hehe~" },
                WorkEmotes = new[] { "💪", "🌟", "🔧" }, WorkLines = new[] { "You got this!", "Nice work!", "Power through!" } } },
            { "kid", new Persona {
                Speed = 62, ClimbSpeed = 50, Jump = 300, Climber = true,
                Acts = new Dictionary<string, double> { {"idle",2},{"walk",3},{"run",2},{"climb",1.5},{"perch",1.8},{"dance",1.2},{"hops",1},{"social",3},{"sit",0.6} },
                Click = new[] { "Hehe!", "That tickles!", "Again! Again!", "Hi hi!", "Am I a spy yet?" },
                ClickEmote = new[] { "✨", "😆", "🤩" }, ClimbEmote = new[] { "😆", "🙌", "👀" },
                PetLines = new[] { "Hehehe!", "More head pats!" },
                WorkEmotes = new[] { "✨", "📝", "🤩" }, WorkLines = new[] { "So busy!", "You're so fast!", "Can I help?" } } },
            { "dog", new Persona {
                Speed = 85, ClimbSpeed = 0, Jump = 140, Climber = false,
                Acts = new Dictionary<string, double> { {"idle",2},{"walk",3},{"run",2},{"sniff",2},{"spin",0.8},{"sit",1.2},{"follow",2},{"social",1.5},{"nap",0.8},{"perch",0.5} },
                Click = new[] { "Woof!", "Arf!", "*wag wag*", "Bork!" },
                ClickEmote = new[] { "🐾", "💕", "❗" }, ClimbEmote = new string[0],
                PetLines = new[] { "*happy wiggle*", "Awoo~" },
                WorkEmotes = new[] { "🐾", "😊", "🦴" }, WorkLines = new[] { "Woof!", "*happy panting*" } } },
        };

        static readonly Dictionary<string, Expression> Exprs = new Dictionary<string, Expression>
        {
            { "neutral", new Expression("open", "smile") },
            { "happy", new Expression("happy", "grin", b: true) },
            { "love", new Expression("heart", "smile", b: true) },
            { "excited", new Expression("star", "grin", b: true) },
            { "surprised", new Expression("wide", "o") },
            { "scared", new Expression("wide", "wavy", s: true) },
            { "nervous", new Expression("open", "wavy", s: true) },
            { "sad", new Expression("sad", "frown") },
            { "sleepy", new Expression("closed", "o") },
            { "dizzy", new Expression("x", "wavy") },
            { "smug", new Expression("open", "smirk", shades: true) },
            { "angry", new Expression("angry", "frown") },
            { "focused", new Expression("open", "flat") },
            { "shy", new Expression("happy", "smile", b: true) },
            { "content", new Expression("closed", "smile") },
        };

        static readonly Dictionary<string, string> ExprEmoji = new Dictionary<string, string>
        {
            {"happy","😄"},{"love","😍"},{"excited","🤩"},{"surprised","😮"},{"scared","😱"},{"nervous","😅"},{"sad","😢"},
            {"sleepy","😴"},{"dizzy","😵"},{"smug","😎"},{"angry","😠"},{"shy","😊"},{"content","😌"},
        };

        static readonly Dictionary<string, StayDef> Stays = new Dictionary<string, StayDef>
        {
            { "sit", new StayDef { State = "sit", TMin = 4, TMax = 9 } },
            { "read", new StayDef { State = "sit", TMin = 6, TMax = 12, Prop = "paper", Expr = "focused", Emotes = new[] { "📰", "🤔", "☕" }, EmoteChance = 0.5 } },
            { "tinker", new StayDef { State = "tinker", TMin = 3, TMax = 6, Prop = "wrench", Expr = "focused", Emotes = new[] { "🔧", "⚙️", "💡" } } },
            { "train", new StayDef { State = "kick", TMin = 2.5, TMax = 4, Expr = "focused", Emotes = new[] { "💪", "🔥" }, Says = new[] { "Hi-yah!", "One more set!" } } },
            { "dance", new StayDef { State = "dance", TMin = 3, TMax = 5, Expr = "happy", Emotes = new[] { "🎵", "🎶", "✨" }, EmoteChance = 0.9 } },
            { "wave", new StayDef { State = "wave", TMin = 1.3, TMax = 1.9, Expr = "happy", Emotes = new[] { "👋" }, EmoteChance = 1 } },
            { "spin", new StayDef { State = "spin", TMin = 1.2, TMax = 2, Expr = "excited", Emotes = new[] { "🌀", "❗" } } },
            { "recover", new StayDef { State = "idle", TMin = 2, TMax = 2.5 } },
            { "hops", new StayDef { State = "idle", TMin = 1.6, TMax = 2.4, Expr = "excited", Tick = "hops" } },
            { "sniff", new StayDef { State = "sniff", TMin = 2, TMax = 4, Emotes = new[] { "👃", "❓" }, EmoteChance = 0.4, Tick = "sniff" } },
            { "nap", new StayDef { State = "sleep", TMin = 8, TMax = 15, Tick = "snore" } },
            { "sleep", new StayDef { State = "sleep", TMin = 25, TMax = 40, Tick = "snore" } },
        };

        public readonly string Id;
        public readonly Persona P;
        public Sprite View;
        public double X, Y, Vx, Vy, W, H, Facing = 1, Tilt, HeldVX, MinX, MaxX;
        public bool Grounded, Clinging, Entering = true, Thrown, Bounced, Dead, Hovering;
        public double FirstImpact, PlatformSince, PlatLeft = double.NaN;
        public int WallSide, platChecks;
        public IntPtr Platform = IntPtr.Zero;
        public string State = "", Expr, Prop = "";
        double exprUntil;
        public Act Act;
        public readonly List<Act> Queue = new List<Act>();

        // pointer
        bool pressed, dragging;
        double pressX0, pressY0, offX, offY, lastClick;
        readonly List<double[]> samples = new List<double[]>();
        // petting
        int petDir, petCount;
        double petSince, petCool;
        public double LastCursorX = double.NaN;

        public Member(string id)
        {
            Id = id;
            P = Personas[id];
            Facing = World.Chance(0.5) ? 1 : -1;
            View = new Sprite(this);
            BuildLook();
            var wa = World.WA;
            X = World.Rand(wa.Left + W, Math.Max(wa.Left + W + 1, wa.Right - W));
            Y = wa.Top - World.Rand(30, 280);
            MinX = wa.Left + W / 2;
            MaxX = wa.Right - W / 2;
            SetState("fall");
            View.Place(X, Y, Facing, 0, false, false, false, Id == "dog", State);
            View.Show();
            Native.BringTopmost(View.Handle);
        }

        public string Name { get { return World.Settings.NameOf(Id); } }

        public void BuildLook()
        {
            double w;
            View.SetLook(World.ArtSvg[Id], World.Settings.SkinPath(Id), World.BaseHeight[Id] * World.Settings.Scale, out w);
            H = World.BaseHeight[Id] * World.Settings.Scale;
            W = w;
            View.SetName(Name);
            ApplyExpr();
        }

        public void Destroy()
        {
            Dead = true;
            EndAct();
            View.Close();
        }

        // ----- looks -----
        public void SetState(string s)
        {
            if (s == State) return;
            State = s;
            ApplyExpr();
        }

        string BaseExpr()
        {
            if (State == "sleep") return "sleepy";
            if (Act != null && Act.Expr != null) return Act.Expr;
            return "neutral";
        }

        public string CurrentExpr { get { return Expr ?? BaseExpr(); } }

        public void ApplyExpr()
        {
            string name = CurrentExpr;
            Expression e;
            if (!Exprs.TryGetValue(name, out e)) e = Exprs["neutral"];
            string emoji;
            ExprEmoji.TryGetValue(name, out emoji);
            View.ApplyExpr(e.E, e.M, e.B, e.S, e.Shades, Prop, emoji);
        }

        public bool Happy { get { var n = CurrentExpr; return n == "happy" || n == "love" || n == "excited" || n == "shy"; } }

        public void SetExpr(string name, double ms = 2000)
        {
            Expr = name;
            exprUntil = World.Now + ms / 1000;
            ApplyExpr();
        }

        public void SetProp(string p, double ms = 0)
        {
            Prop = p ?? "";
            ApplyExpr();
            if (p != null && p.Length > 0 && ms > 0) World.Later(ms, delegate { if (Prop == p) SetProp(""); });
        }

        public void Emote(string sym, double ms = 1800) { if (!string.IsNullOrEmpty(sym)) View.Emote(sym, ms); }
        public void Say(string text, double ms = 2600) { if (!string.IsNullOrEmpty(text)) View.Say(text, ms); }
        public void Particles(string sym, int n = 3) { View.Particles(sym, n); }
        public void Hop() { if (Grounded) View.AddFx("hop", 480); }
        public void Squash() { View.AddFx("squash", 260); }
        public void Shake(double ms = 600) { View.AddFx("shake", ms); }

        // ----- actions -----
        public bool IsFree()
        {
            if (Dead || !Grounded || State == "held") return false;
            var a = Act;
            return a == null || (!a.Locked && (a.Name == "idle" || a.Name == "walk" || a.Name == "sit" || a.Name == "sniff"));
        }

        public void StartAct(Act spec)
        {
            EndAct();
            var a = spec.Clone();
            Act = a;
            StartActImpl(a);
            ApplyExpr();
        }

        public void EndAct()
        {
            var a = Act;
            if (a == null) return;
            Act = null;
            StayDef sd;
            if (Stays.TryGetValue(a.Name, out sd) && sd.Prop != null) SetProp("");
        }

        public void FinishAct()
        {
            EndAct();
            Next();
        }

        public void Order(IEnumerable<Act> seq, bool comeDown)
        {
            Queue.Clear();
            Queue.AddRange(seq);
            if (State == "held") return;
            if (Clinging) { LetGo(); return; }
            if (!Grounded) return;
            if (comeDown && Platform != IntPtr.Zero) { HopDown(); return; }
            EndAct();
            Next();
        }

        public void Next()
        {
            if (Dead) return;
            if (Clinging && Queue.Count == 0)
            {
                StartAct(new Act("climb") { Phase = "hold", T = World.Rand(1.5, 3.5) });
                return;
            }
            if (!Grounded) return;
            if (Queue.Count > 0)
            {
                var q = Queue[0];
                Queue.RemoveAt(0);
                StartAct(q);
                return;
            }
            if (World.IdleSeconds > 120)
            {
                World.Dozing = true;
                StartAct(new Act("sleep"));
                return;
            }

            var acts = new Dictionary<string, double>(P.Acts);
            bool onPlat = Platform != IntPtr.Zero;
            if (onPlat)
            {
                acts.Remove("climb"); acts.Remove("perch"); acts.Remove("run"); acts.Remove("follow");
                acts["leave"] = (World.Now - PlatformSince) > 25 ? 8 : 1.2;
            }
            if (!World.Settings.Roam) acts.Remove("perch");
            var partners = World.Members.Values.Where(o => o != this && o.IsFree() && o.Platform == Platform).ToList();
            if (partners.Count == 0) acts.Remove("social");
            Member kid;
            World.Members.TryGetValue("kid", out kid);
            if (kid == null || !kid.Grounded || kid.Platform != Platform) acts.Remove("follow");

            string name = World.Weighted(acts);
            switch (name)
            {
                case "walk": StartAct(new Act("walk")); break;
                case "run": StartAct(new Act("walk") { Run = true }); break;
                case "sneak": StartAct(new Act("walk") { Sneak = true, Expr = "smug" }); break;
                case "social": StartAct(new Act("social") { Who = World.Pick(partners) }); break;
                case "follow": StartAct(new Act("follow") { Who = kid }); break;
                case "leave":
                    {
                        bool left = World.Chance(0.5);
                        StartAct(new Act("walk") { X = left ? MinX - W : MaxX + W, OffEdge = true });
                        break;
                    }
                default: StartAct(new Act(name)); break;
            }
        }

        void StartActImpl(Act a)
        {
            StayDef sd;
            if (Stays.TryGetValue(a.Name, out sd))
            {
                SetState(a.State ?? sd.State);
                if (double.IsNaN(a.T)) a.T = World.Rand(sd.TMin, sd.TMax);
                if (a.Expr == null) a.Expr = sd.Expr;
                if (sd.Prop != null) SetProp(sd.Prop);
                if (sd.Emotes != null && World.Chance(sd.EmoteChance)) Emote(World.Pick(sd.Emotes), 2000);
                if (sd.Says != null && World.Chance(sd.SayChance)) Say(World.Pick(sd.Says));
                a.El = 0;
                return;
            }
            switch (a.Name)
            {
                case "idle":
                    SetState("idle");
                    if (double.IsNaN(a.T)) a.T = World.Rand(1.5, 4);
                    break;
                case "walk":
                    a.Speed = (a.Run ? 2.1 : a.Sneak ? 0.55 : 1) * P.Speed;
                    if (double.IsNaN(a.X))
                    {
                        a.X = World.Rand(MinX, MaxX);
                        if (Math.Abs(a.X - X) < 40) a.X = World.Clamp(X + (World.Chance(0.5) ? -1 : 1) * World.Rand(60, 200), MinX, MaxX);
                    }
                    if (!a.OffEdge) a.X = World.Clamp(a.X, MinX, MaxX);
                    if (double.IsNaN(a.Timeout)) a.Timeout = 14;
                    SetState(a.Run ? "run" : a.Sneak ? "sneak" : "walk");
                    break;
                case "climb":
                    if (!P.Climber) { a.Fail = true; break; }
                    if (a.Side == 0) a.Side = X < World.WA.Left + World.WA.Width / 2 ? -1 : 1;
                    if (a.Phase == null) a.Phase = "approach";
                    a.TopY = World.Rand(World.WA.Top + H + 30, Math.Max(World.WA.Top + H + 40, World.WA.Top + World.WA.Height * 0.6));
                    if (a.Phase == "approach") SetState("walk");
                    if (a.Phase == "hold") SetState("cling");
                    break;
                case "perch":
                    a.Target = World.FindPerch(this);
                    if (a.Target == null) { a.Fail = true; break; }
                    a.X = World.Clamp(World.Rand(a.Target.Left, a.Target.Right), MinX, MaxX);
                    a.Timeout = 9;
                    SetState("walk");
                    break;
                case "social":
                    a.T = 8;
                    SetState("walk");
                    break;
                case "follow":
                    if (double.IsNaN(a.T)) a.T = World.Rand(4, 8);
                    SetState("walk");
                    break;
                case "chase":
                    SetState("run");
                    a.Expr = "excited";
                    Emote("🐾");
                    break;
                case "interact":
                    a.El = 0;
                    a.Si = 0;
                    a.Locked = true;
                    World.StartKind(this, a);
                    break;
            }
        }

        // returns true when the action is finished
        bool UpdateAct(Act a, double dt)
        {
            StayDef sd;
            if (Stays.TryGetValue(a.Name, out sd))
            {
                a.El += dt;
                if (sd.Tick == "hops" && a.El > a.NextHop) { a.NextHop = a.El + 0.55; Hop(); }
                else if (sd.Tick == "sniff")
                {
                    X += Facing * 18 * dt;
                    if (X <= MinX || X >= MaxX) Facing *= -1;
                    X = World.Clamp(X, MinX, MaxX);
                }
                else if (sd.Tick == "snore" && a.El > Math.Max(1, a.NextZ)) { a.NextZ = a.El + World.Rand(3, 5); Emote("💤", 2200); }
                return a.El >= a.T;
            }
            switch (a.Name)
            {
                case "idle":
                    LookAtCursor();
                    return (a.T -= dt) <= 0;

                case "walk":
                    {
                        a.Timeout -= dt;
                        double dx = a.X - X;
                        if (Math.Abs(dx) < 2 || a.Timeout <= 0) return true;
                        Facing = Math.Sign(dx);
                        X += Math.Sign(dx) * Math.Min(Math.Abs(dx), a.Speed * dt);
                        if (!a.OffEdge) X = World.Clamp(X, MinX, MaxX);
                        else if (Platform != IntPtr.Zero && (X < MinX - W * 0.4 || X > MaxX + W * 0.4)) FallOff();
                        return false;
                    }

                case "climb":
                    {
                        if (a.Fail) return true;
                        if (a.Phase == "approach")
                        {
                            double wx = a.Side < 0 ? MinX : MaxX;
                            double dx = wx - X;
                            if (Math.Abs(dx) > 2)
                            {
                                Facing = Math.Sign(dx);
                                X += Math.Sign(dx) * Math.Min(Math.Abs(dx), P.Speed * 1.2 * dt);
                                return false;
                            }
                            StartCling(a.Side);
                            Act = a;
                            a.Phase = "up";
                            SetState("climb");
                            return false;
                        }
                        if (a.Phase == "up")
                        {
                            Y -= P.ClimbSpeed * dt;
                            if (Y <= a.TopY)
                            {
                                Y = a.TopY;
                                a.Phase = "hold";
                                a.T = World.Rand(2, 5);
                                SetState("cling");
                                if (World.Chance(0.45) && P.ClimbEmote.Length > 0) Emote(World.Pick(P.ClimbEmote));
                            }
                            return false;
                        }
                        if (a.Phase == "hold")
                        {
                            if ((a.T -= dt) > 0) return false;
                            if (World.Chance(0.5)) { a.Phase = "down"; SetState("climb"); return false; }
                            LetGo();
                            return false;
                        }
                        Y += P.ClimbSpeed * 1.4 * dt;
                        if (Y >= World.Floor)
                        {
                            Y = World.Floor;
                            Clinging = false;
                            Grounded = true;
                            Platform = IntPtr.Zero;
                            return true;
                        }
                        return false;
                    }

                case "perch":
                    {
                        if (a.Fail) return true;
                        a.Timeout -= dt;
                        IntPtr h = a.Target.Hwnd;
                        if (!Native.IsLandable(h) || a.Timeout < 0) return true;
                        Rect r = Native.Frame(h);
                        if (r.Top < World.WA.Top + H + 10 || r.Top > World.Floor - 40 || World.Floor - r.Top > P.Jump) return true;
                        double dx = a.X - X;
                        if (Math.Abs(dx) > 2)
                        {
                            Facing = Math.Sign(dx);
                            X += Math.Sign(dx) * Math.Min(Math.Abs(dx), P.Speed * 1.3 * dt);
                            SetState("walk");
                            return false;
                        }
                        if (X < r.Left + 4 || X > r.Right - 4) return true;
                        double hgt = Y - r.Top;
                        EndAct();
                        Grounded = false;
                        Platform = IntPtr.Zero;
                        Vx = 0;
                        Vy = -Math.Sqrt(2 * G * (hgt + 28));
                        SetState("fall");
                        if (Id == "kid" && World.Chance(0.4)) Say(World.Pick(new[] { "Hup!", "Up we go!" }), 1200);
                        else if (Id == "dog" && World.Chance(0.5)) Emote("🐾");
                        return false;
                    }

                case "social":
                    {
                        var o = a.Who;
                        if (o == null || o.Dead || !o.Grounded || o.Platform != Platform || !o.IsFree()) return true;
                        if ((a.T -= dt) <= 0) return true;
                        double gap = (o.W + W) / 2 + 2;
                        double dx = o.X - X;
                        if (Math.Abs(dx) <= gap + 4) { World.StartInteraction(this, o); return false; }
                        double tx = o.X - Math.Sign(dx) * gap;
                        Facing = Math.Sign(dx);
                        X += Math.Sign(tx - X) * Math.Min(Math.Abs(tx - X), P.Speed * 1.2 * dt);
                        X = World.Clamp(X, MinX, MaxX);
                        SetState("walk");
                        return false;
                    }

                case "follow":
                    {
                        var o = a.Who;
                        if (o == null || o.Dead || (a.T -= dt) <= 0) return true;
                        if (!o.Grounded || o.Platform != Platform) { SetState("idle"); return false; }
                        int side = X < o.X ? -1 : 1;
                        double tx = World.Clamp(o.X + side * (o.W / 2 + W / 2 + 4), MinX, MaxX);
                        double dx = tx - X;
                        if (Math.Abs(dx) < 4)
                        {
                            SetState("idle");
                            if (o.X != X) Facing = Math.Sign(o.X - X);
                            return false;
                        }
                        bool fast = Math.Abs(dx) > 120;
                        SetState(fast ? "run" : "walk");
                        Facing = Math.Sign(dx);
                        X += Math.Sign(dx) * Math.Min(Math.Abs(dx), P.Speed * (fast ? 2 : 1) * dt);
                        return false;
                    }

                case "chase":
                    {
                        if ((a.T -= dt) <= 0) return true;
                        double tx = World.Clamp(World.Cursor.X, MinX, MaxX);
                        double dx = tx - X;
                        if (Math.Abs(dx) < 6) { SetState("idle"); return false; }
                        SetState("run");
                        Facing = Math.Sign(dx);
                        X += Math.Sign(dx) * Math.Min(Math.Abs(dx), P.Speed * 2.3 * dt);
                        return false;
                    }

                case "interact":
                    {
                        a.El += dt;
                        if (a.Role == 0 && a.Script != null)
                        {
                            while (a.Si < a.Script.Length && a.El >= a.Si * 1.6 + 0.2)
                            {
                                var speaker = a.Si % 2 == 0 ? this : a.Partner;
                                if (!speaker.Dead) speaker.Say(a.Script[a.Si], 1800);
                                a.Si++;
                            }
                        }
                        if (a.El >= a.T)
                        {
                            if (a.Kind == "chat") SetExpr("happy", 1200);
                            return true;
                        }
                        return false;
                    }
            }
            return true;
        }

        // ----- movement helpers -----
        public void StartCling(int side)
        {
            EndAct();
            Clinging = true;
            Grounded = false;
            Platform = IntPtr.Zero;
            WallSide = side;
            Facing = side;
            Vx = 0;
            Vy = 0;
            Thrown = false;
            SetState("cling");
        }

        public void LetGo()
        {
            Clinging = false;
            EndAct();
            Vx = -WallSide * World.Rand(60, 140);
            Vy = -80;
            SetState("fall");
            if (Id == "kid" && World.Chance(0.6)) Say(World.Pick(new[] { "Wheee!", "Cannonball!" }), 1400);
            else if (World.Chance(0.3)) Emote(World.Pick(new[] { "😆", "🤸" }));
        }

        public void HopDown()
        {
            EndAct();
            Grounded = false;
            Platform = IntPtr.Zero;
            Vy = -260;
            Vx = World.Rand(-60, 60);
            SetState("fall");
        }

        public void FallOff()
        {
            EndAct();
            Grounded = false;
            Platform = IntPtr.Zero;
            Vy = 0;
            SetState("fall");
        }

        void LookAtCursor()
        {
            if (World.Now - World.LastCursorMove > 2.5) return;
            var c = World.Cursor;
            if (Math.Abs(c.X - X) < 220 && Math.Abs(c.Y - Y) < 260 && c.X != X) Facing = Math.Sign(c.X - X);
        }

        bool UpdateSurface()
        {
            var wa = World.WA;
            if (Platform != IntPtr.Zero)
            {
                if (!Native.IsLandable(Platform)) return false;
                Rect r = Native.Frame(Platform);
                if (r.Width < 2 || r.Top < wa.Top + H * 0.5 || r.Top > World.Floor - 4) return false;
                if (!double.IsNaN(PlatLeft)) X += r.Left - PlatLeft; // ride along when the window moves
                PlatLeft = r.Left;
                if (X < r.Left - 2 || X > r.Right + 2) return false;
                if (++platChecks % 15 == 0 && !World.TopVisibleAt(Platform, X, r.Top)) return false;
                Y = r.Top;
                MinX = Math.Max(r.Left, wa.Left) + W * 0.35;
                MaxX = Math.Min(r.Right, wa.Right) - W * 0.35;
                if (MinX > MaxX) MinX = MaxX = (MinX + MaxX) / 2;
                return true;
            }
            Y = World.Floor;
            MinX = wa.Left + W / 2;
            MaxX = wa.Right - W / 2;
            X = World.Clamp(X, MinX, MaxX);
            return true;
        }

        void Physics(double dt)
        {
            var wa = World.WA;
            double y0 = Y;
            Vy = Math.Min(Vy + G * dt, 2600);
            X += Vx * dt;
            Y += Vy * dt;
            double half = W / 2;
            if (X < wa.Left + half || X > wa.Right - half)
            {
                int side = X < wa.Left + half ? -1 : 1;
                X = side < 0 ? wa.Left + half : wa.Right - half;
                if (P.Climber && Thrown && Math.Abs(Vx) > 300 && Y < World.Floor - 60 && World.Chance(0.6))
                {
                    StartCling(side);
                    SetExpr("surprised", 900);
                    Emote("❗", 900);
                    return;
                }
                Vx = -side * Math.Abs(Vx) * 0.45;
            }
            if (!Entering && Y < wa.Top + H)
            {
                Y = wa.Top + H;
                if (Vy < 0) Vy = -Vy * 0.3;
            }
            if (Vy > 0 && World.Settings.Roam && !Entering)
            {
                IntPtr hit;
                double top;
                if (World.FindLanding(this, y0, Y, out hit, out top)) { Land(hit, top); return; }
            }
            if (Y >= World.Floor) { Land(IntPtr.Zero, World.Floor); return; }
            if (State != "fall") SetState("fall");
        }

        void Land(IntPtr platform, double top)
        {
            double impact = Vy;
            Y = top;
            if (impact > 800 && !Bounced)
            {
                Bounced = true;
                FirstImpact = impact;
                Vy = -impact * 0.28;
                Vx *= 0.6;
                Y = top - 0.5;
                return;
            }
            double hardest = Math.Max(impact, Bounced ? FirstImpact : 0);
            bool wasThrown = Thrown, wasEntering = Entering;
            Bounced = false;
            Thrown = false;
            Entering = false;
            Vx = 0;
            Vy = 0;
            Grounded = true;
            Platform = platform;
            PlatLeft = double.NaN;
            PlatformSince = World.Now;
            SetState("idle");
            Squash();
            if (!wasEntering && (hardest > 1250 || (wasThrown && hardest > 1000))) { Dizzy(); return; }
            if (wasThrown && World.Chance(0.4)) Emote(World.Pick(new[] { "😳", "😅", "✨" }));
            Next();
        }

        void Dizzy()
        {
            Queue.Insert(0, new Act("recover"));
            SetExpr("dizzy", 2400);
            Emote("💫", 2200);
            Next();
            var others = World.Members.Values.Where(o => o != this && o.Grounded && !o.Dead).ToList();
            if (others.Count == 0) return;
            var w = World.Pick(others);
            var me = this;
            World.Later(500, delegate
            {
                if (w.Dead) return;
                if (w.Id == "dog")
                {
                    w.Emote("❗");
                    if (w.IsFree() && w.Platform == me.Platform) w.StartAct(new Act("follow") { Who = me, T = 3 });
                }
                else if (w.Id == "kid" && World.Chance(0.5))
                {
                    w.SetExpr("happy", 1500);
                    w.Say(World.Pick(new[] { "Haha! Again!", "Whoa, cool!" }), 1600);
                }
                else
                {
                    w.SetExpr("surprised", 1500);
                    w.Say(World.Pick(new[] { "You okay?!", "Yikes!" }), 1600);
                }
            });
        }

        public void Update(double dt)
        {
            if (Expr != null && World.Now > exprUntil) { Expr = null; ApplyExpr(); }
            if (State == "held")
            {
                HeldVX *= Math.Pow(0.02, dt);
                double target = World.Clamp(-HeldVX * 0.025, -35, 35);
                Tilt += (target - Tilt) * Math.Min(1, dt * 10);
                return;
            }
            var wa = World.WA;
            if (Clinging)
            {
                X = WallSide < 0 ? wa.Left + W * 0.32 : wa.Right - W * 0.32;
                if (Y < wa.Top + H + 4) Y = wa.Top + H + 4;
                if (Y > World.Floor)
                {
                    Y = World.Floor;
                    Clinging = false;
                    Grounded = true;
                    Platform = IntPtr.Zero;
                    EndAct();
                }
            }
            else if (Grounded && !UpdateSurface()) FallOff();

            if (!Grounded && !Clinging) { Physics(dt); return; }
            if (Act == null) Next();
            var a = Act;
            if (a != null && UpdateAct(a, dt) && Act == a) FinishAct();
        }

        public void Render()
        {
            double tilt = 0;
            if (State == "held") tilt = Tilt;
            else if (Thrown && !Grounded) tilt = World.Clamp(Vx * 0.03, -25, 25);
            View.Place(X, Y, Facing, tilt, State == "held", Hovering, Happy, Id == "dog", State);
        }

        // ----- cursor interaction -----
        public void OnEnter()
        {
            Hovering = true;
            if (State == "held" || State == "sleep" || Expr != null) return;
            SetExpr(Id == "dad" ? "focused" : "happy", 1200);
        }

        public void OnDown()
        {
            var c = Native.Cursor();
            pressed = true;
            dragging = false;
            pressX0 = c.X;
            pressY0 = c.Y;
            samples.Clear();
            samples.Add(new[] { c.X, c.Y, World.Now });
        }

        public void OnDrag()
        {
            if (!pressed) return;
            var c = Native.Cursor();
            double t = World.Now;
            samples.Add(new[] { c.X, c.Y, t });
            while (samples.Count > 2 && t - samples[0][2] > 0.09) samples.RemoveAt(0);
            if (!dragging && Math.Sqrt((c.X - pressX0) * (c.X - pressX0) + (c.Y - pressY0) * (c.Y - pressY0)) > 5) StartDrag();
            if (dragging)
            {
                X = c.X - offX;
                Y = c.Y - offY;
                var s0 = samples[0];
                double dts = t - s0[2];
                if (dts > 0.005) HeldVX = (c.X - s0[0]) / dts;
            }
        }

        void StartDrag()
        {
            dragging = true;
            offX = pressX0 - X;
            offY = pressY0 - Y;
            EndAct();
            Queue.Clear();
            Grounded = false;
            Clinging = false;
            Platform = IntPtr.Zero;
            Vx = Vy = 0;
            Tilt = 0;
            HeldVX = 0;
            SetState("held");
            if (Id == "dog")
            {
                SetExpr(World.Pick(new[] { "happy", "surprised" }), 600000);
                Emote(World.Pick(new[] { "❗", "🐾" }));
            }
            else
            {
                SetExpr(World.Pick(new[] { "surprised", "nervous", "happy" }), 600000);
                Emote(World.Pick(new[] { "😳", "❗", "🙌" }));
                if (World.Chance(0.35)) Say(World.Pick(new[] { "Whoa!", "Put me down!", "I can fly!", "Hey!" }), 1500);
            }
            var watchers = World.Members.Values.Where(o => o != this && o.Grounded).ToList();
            if (watchers.Count > 0 && World.Chance(0.6))
            {
                var w = World.Pick(watchers);
                World.Later(300, delegate
                {
                    w.SetExpr("surprised", 1400);
                    w.Emote(w.Id == "dog" ? "❗" : World.Pick(new[] { "😮", "👀" }));
                });
            }
        }

        public void OnUp()
        {
            if (!pressed) return;
            pressed = false;
            if (dragging) Release();
            else OnClick();
            dragging = false;
        }

        public void OnCaptureLost()
        {
            if (pressed && dragging) { pressed = false; dragging = false; Release(); }
            pressed = false;
        }

        void Release()
        {
            double vx = 0, vy = 0;
            if (samples.Count > 1)
            {
                var a = samples[0];
                var b = samples[samples.Count - 1];
                double dts = b[2] - a[2];
                if (dts > 0.005 && World.Now - b[2] < 0.12)
                {
                    vx = World.Clamp((b[0] - a[0]) / dts, -1800, 1800);
                    vy = World.Clamp((b[1] - a[1]) / dts, -1800, 1800);
                }
            }
            Expr = null;
            Tilt = 0;
            Vx = vx;
            Vy = vy;
            Thrown = Math.Sqrt(vx * vx + vy * vy) > 250;
            Entering = false;
            SetState("fall");
            var wa = World.WA;
            int nearWall = X < wa.Left + W * 0.9 ? -1 : X > wa.Right - W * 0.9 ? 1 : 0;
            if (nearWall != 0 && P.Climber && Y < World.Floor - 40 && !Thrown)
            {
                StartCling(nearWall);
                Y = Math.Max(Y, wa.Top + H + 4);
                Next();
            }
        }

        void OnClick()
        {
            double t = World.Now;
            bool dbl = t - lastClick < 0.33;
            lastClick = t;
            if (State == "sleep")
            {
                SetExpr("surprised", 1200);
                Emote("❗");
                FinishAct();
                return;
            }
            if (dbl) { Special(); return; }
            Hop();
            SetExpr(Id == "dad" ? World.Pick(new[] { "smug", "focused", "happy" }) : World.Pick(new[] { "happy", "excited", "surprised" }), 1600);
            if (World.Chance(0.55)) Say(World.Pick(P.Click), 1800);
            else Emote(World.Pick(P.ClickEmote));
            if (Id == "dog" && World.Chance(0.3) && Grounded && Platform == IntPtr.Zero)
                World.Later(400, delegate { if (IsFree()) StartAct(new Act("chase") { T = 4 }); });
        }

        void Special()
        {
            if (!Grounded) return;
            switch (Id)
            {
                case "dad":
                    Say("This conversation never happened.", 2200);
                    StartAct(new Act("walk") { Sneak = true, Expr = "smug", X = X + Facing * 120 });
                    break;
                case "mom":
                    Say("Hi-yah!", 1400);
                    StartAct(new Act("train") { T = 1.6 });
                    break;
                case "kid":
                    StartAct(new Act("dance") { T = 2.5 });
                    break;
                case "dog":
                    Say("Arf arf!", 1400);
                    StartAct(new Act("spin") { T = 1.5 });
                    break;
            }
        }

        // Called every frame with the cursor position: wiggling over a character pets them.
        public void TrackPetting(Point c)
        {
            double prev = LastCursorX;
            LastCursorX = c.X;
            if (State == "held" || pressed || double.IsNaN(prev)) return;
            if (c.X < X - W / 2 || c.X > X + W / 2 || c.Y < Y - H || c.Y > Y) return;
            double t = World.Now;
            if (t < petCool) return;
            double mx = c.X - prev;
            if (Math.Abs(mx) < 2) return;
            int dir = Math.Sign(mx);
            if (t - petSince > 1.3) { petCount = 0; petSince = t; }
            if (dir != petDir) { petDir = dir; petCount++; }
            if (petCount >= 5)
            {
                petCount = 0;
                petCool = t + 1.8;
                Petted();
            }
        }

        void Petted()
        {
            if (State == "sleep")
            {
                SetExpr("content", 2500);
                Particles("💗", 2);
                return;
            }
            SetExpr(Id == "dad" ? "shy" : "love", 2400);
            Particles(Id == "dog" ? "💕" : World.Pick(new[] { "💗", "💕", "✨" }), 3);
            if (World.Chance(0.4)) Say(World.Pick(P.PetLines), 1600);
        }
    }
}
