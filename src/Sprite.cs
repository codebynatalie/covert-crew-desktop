using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Path = System.Windows.Shapes.Path;

namespace CovertCrew
{
    // The little transparent, always-on-top window that one character lives in.
    class Sprite : Window
    {
        static readonly Brush Ink = Frozen(Color.FromRgb(0x2a, 0x22, 0x30));
        static readonly Brush Paper = Frozen(Colors.White);
        static readonly Brush TagBg = Frozen(Color.FromArgb(0xd9, 0x2a, 0x22, 0x30));
        const double FootPad = 2;

        readonly Member m;
        readonly Canvas stage = new Canvas();
        readonly Canvas body = new Canvas();
        readonly MatrixTransform bodyTf = new MatrixTransform();
        SvgRig rig;
        public double VbW, VbH;
        double s;
        public double WinW, WinH;
        public IntPtr Handle;

        readonly Border emoteB, sayB, tagB;
        readonly TextBlock emoteT, sayT, tagT, faceEmoji;
        readonly Path sayTail;
        double emoteUntil, sayUntil;

        class Fx { public string Kind; public double Start, Dur; }
        readonly List<Fx> effects = new List<Fx>();

        public Sprite(Member member)
        {
            m = member;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Title = "Covert Crew - " + member.Id;
            Content = stage;
            UseLayoutRounding = true;

            body.RenderTransform = bodyTf;
            stage.Children.Add(body);

            faceEmoji = new TextBlock { FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 15, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            stage.Children.Add(faceEmoji);

            emoteT = new TextBlock { FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 16, Foreground = Ink };
            emoteB = Bubble(emoteT, new CornerRadius(12, 12, 12, 3), new Thickness(5, 1, 5, 1));
            sayT = new TextBlock { FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI"), FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Ink, TextWrapping = TextWrapping.Wrap, MaxWidth = 172, TextAlignment = TextAlignment.Center };
            sayB = Bubble(sayT, new CornerRadius(12), new Thickness(9, 4, 9, 4));
            sayTail = new Path { Data = Geometry.Parse("M0 0 L12 0 L6 7 Z"), Fill = Ink, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            stage.Children.Add(sayTail);
            tagT = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Paper };
            tagB = new Border { Background = TagBg, CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 2), Child = tagT, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            stage.Children.Add(tagB);

            SourceInitialized += delegate
            {
                Handle = new WindowInteropHelper(this).Handle;
                int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
                Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
            };
            MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e) { CaptureMouse(); m.OnDown(); e.Handled = true; };
            MouseMove += delegate { if (IsMouseCaptured) m.OnDrag(); };
            MouseLeftButtonUp += delegate(object o, MouseButtonEventArgs e) { bool had = IsMouseCaptured; ReleaseMouseCapture(); if (had) m.OnUp(); };
            LostMouseCapture += delegate { m.OnCaptureLost(); };
            MouseEnter += delegate { m.OnEnter(); };
            MouseLeave += delegate { m.Hovering = false; };
        }

        static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        Border Bubble(TextBlock content, CornerRadius radius, Thickness pad)
        {
            var b = new Border
            {
                Background = Paper,
                BorderBrush = Ink,
                BorderThickness = new Thickness(2),
                CornerRadius = radius,
                Padding = pad,
                Child = content,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 1),
                RenderTransform = new ScaleTransform(1, 1),
            };
            stage.Children.Add(b);
            return b;
        }

        // Build (or rebuild) the character's look at the given on-screen height.
        public void SetLook(string svg, string skinPath, double height, out double width)
        {
            body.Children.Clear();
            rig = null;
            bool skinned = false;
            if (skinPath != null && File.Exists(skinPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(skinPath);
                    bmp.EndInit();
                    bmp.Freeze();
                    VbW = bmp.PixelWidth;
                    VbH = bmp.PixelHeight;
                    var img = new Image { Source = bmp, Width = VbW, Height = VbH, Stretch = Stretch.Fill };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                    body.Children.Add(img);
                    skinned = true;
                }
                catch (Exception) { skinned = false; }
            }
            if (!skinned)
            {
                rig = SvgRig.Load(svg);
                VbW = rig.VbW;
                VbH = rig.VbH;
                body.Children.Add(rig.Root);
            }
            faceEmoji.Visibility = skinned ? Visibility.Visible : Visibility.Collapsed;
            body.Effect = new System.Windows.Media.Effects.DropShadowEffect { Direction = 270, ShadowDepth = 1.5, BlurRadius = 0, Opacity = 0.18, Color = Colors.Black };
            s = height / VbH;
            width = VbW * s;
            WinW = Math.Ceiling(width + 440);
            WinH = Math.Ceiling(height + 150);
            Width = WinW;
            Height = WinH;
            stage.Width = WinW;
            stage.Height = WinH;
        }

        public void SetName(string name) { tagT.Text = name; }

        public void ApplyExpr(string eyes, string mouth, bool blush, bool sweat, bool shades, string prop, string emoji)
        {
            faceEmoji.Text = emoji ?? "";
            if (rig == null) return;
            foreach (var n in new[] { "open", "happy", "closed", "wide", "x", "heart", "star", "sad", "angry" }) rig.Show("ev-" + n, n == eyes);
            foreach (var n in new[] { "smile", "grin", "o", "flat", "frown", "wavy", "smirk" }) rig.Show("mv-" + n, n == mouth);
            rig.Show("blush", blush);
            rig.Show("sweat", sweat);
            rig.Show("shades", shades);
            foreach (var n in new[] { "paper", "wrench", "bone" }) rig.Show("prop-" + n, n == prop);
        }

        // ----- bubbles & particles -----
        void Pop(Border b)
        {
            b.Visibility = Visibility.Visible;
            var st = (ScaleTransform)b.RenderTransform;
            var anim = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut } };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        public void Emote(string sym, double ms)
        {
            emoteT.Text = sym;
            emoteUntil = World.Now + ms / 1000;
            Pop(emoteB);
        }

        public void Say(string text, double ms)
        {
            sayT.Text = text;
            sayUntil = World.Now + ms / 1000;
            Pop(sayB);
            sayB.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }

        public void Particles(string sym, int n)
        {
            for (int i = 0; i < n; i++)
            {
                var tb = new TextBlock { Text = sym, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 15, Foreground = Frozen(Color.FromRgb(0xe8, 0x47, 0x5f)), IsHitTestVisible = false, Opacity = 0 };
                var tt = new TranslateTransform();
                tb.RenderTransform = tt;
                Canvas.SetLeft(tb, WinW / 2 - m.W * 0.4 + World.Rand(0, m.W * 0.7));
                Canvas.SetTop(tb, WinH - FootPad - m.H + World.Rand(-5, m.H * 0.25));
                stage.Children.Add(tb);
                var dur = TimeSpan.FromMilliseconds(1100 + i * 150);
                var delay = TimeSpan.FromMilliseconds(i * 140);
                var up = new DoubleAnimation(0, -48, dur) { BeginTime = delay, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
                var side = new DoubleAnimation(0, World.Rand(-14, 14), dur) { BeginTime = delay };
                var fade = new DoubleAnimationUsingKeyFrames { BeginTime = delay, Duration = dur };
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.2)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                var el = tb;
                fade.Completed += delegate { stage.Children.Remove(el); };
                tt.BeginAnimation(TranslateTransform.YProperty, up);
                tt.BeginAnimation(TranslateTransform.XProperty, side);
                tb.BeginAnimation(OpacityProperty, fade);
            }
        }

        public void AddFx(string kind, double ms)
        {
            effects.Add(new Fx { Kind = kind, Start = World.Now, Dur = ms / 1000 });
        }

        // ----- per-frame pose -----
        static double Sn(double t, double half) { return Math.Sin(Math.PI * t / half); }
        static void Rot(List<PartT> parts, double a) { foreach (var p in parts) p.R.Angle = a; }

        public void Pose(string state, double t, bool happy, bool dog, out double ax, out double ay, out double rot, out double tx, out double ty, out double px, out double py)
        {
            ax = 1; ay = 1; rot = 0; tx = 0; ty = 0; px = VbW / 2; py = VbH;
            bool r = rig != null;
            if (r) rig.ResetParts();
            switch (state)
            {
                case "idle":
                    ay = 1 + 0.011 * (1 - Math.Cos(2 * Math.PI * t / 2.6));
                    break;
                case "walk":
                case "sneak":
                case "run":
                    {
                        double per = state == "run" ? 0.22 : state == "sneak" ? 0.7 : 0.42;
                        double sw = 20 * Sn(t, per);
                        if (r)
                        {
                            Rot(rig.LegL, sw); Rot(rig.LegR, -sw); Rot(rig.ArmL, -sw); Rot(rig.ArmR, sw);
                            ty = -0.03 * VbH * Math.Abs(Sn(t, per));
                        }
                        else
                        {
                            rot = 5 * Sn(t, per);
                            ty = -0.04 * VbH * Math.Abs(Sn(t, per));
                        }
                        if (state == "sneak") { ty += 0.05 * VbH; ay = 0.94; }
                        if (state == "run" && dog && r && rig.Ear != null) rig.Ear.R.Angle = 7.5 + 17.5 * Sn(t, 0.18);
                        break;
                    }
                case "climb":
                    if (r)
                    {
                        double c = 22.5 * Sn(t, 0.45);
                        Rot(rig.ArmL, 172.5 + c); Rot(rig.ArmR, -(172.5 - c));
                        Rot(rig.LegL, 8 * Sn(t, 0.45)); Rot(rig.LegR, -8 * Sn(t, 0.45));
                    }
                    else rot = 5 * Sn(t, 0.3);
                    break;
                case "cling":
                    if (r) { Rot(rig.ArmL, 168); Rot(rig.ArmR, -168); Rot(rig.LegL, 10); Rot(rig.LegR, -10); }
                    break;
                case "held":
                    if (r)
                    {
                        Rot(rig.ArmL, 150); Rot(rig.ArmR, -150);
                        double l = dog ? 20 * Sn(t, 0.3) : 8 * Sn(t, 0.5);
                        Rot(rig.LegL, l); Rot(rig.LegR, -l);
                    }
                    else rot = 5 * Sn(t, 0.5);
                    break;
                case "fall":
                    if (r) { Rot(rig.ArmL, 150 + 20 * Sn(t, 0.2)); Rot(rig.ArmR, -(150 - 20 * Sn(t, 0.2))); }
                    break;
                case "sit":
                case "sleep":
                    if (dog)
                    {
                        if (state == "sit") { rot = -13; px = VbW * 0.2; py = VbH * 0.95; }
                        else if (r) { foreach (var p in rig.AllLegs) p.S.ScaleY = 0.35; ty = 0.12 * VbH; }
                    }
                    else if (r && rig.Legs != null)
                    {
                        rig.Legs.S.ScaleY = 0.42;
                        if (rig.Upper != null) rig.Upper.T.Y = (rig.Legs.S.CenterY - LegsTop()) * 0.58;
                    }
                    else if (!r)
                    {
                        if (state == "sit") ay = 0.86;
                        else { rot = -80; tx = -0.3 * VbW; }
                    }
                    if (state == "sleep") ay *= 1 + 0.02 * (1 - Math.Cos(2 * Math.PI * t / 2.4));
                    break;
                case "wave":
                    if (r) Rot(rig.ArmR, -(150 + 20 * Sn(t, 0.28)));
                    break;
                case "dance":
                    rot = -7 * Math.Cos(2 * Math.PI * t / 0.6);
                    ty = -0.09 * VbH * Math.Abs(Math.Sin(2 * Math.PI * t / 0.6));
                    if (r) { Rot(rig.ArmL, 150 + 20 * Sn(t, 0.3)); Rot(rig.ArmR, -(150 + 20 * Sn(t, 0.3))); }
                    break;
                case "kick":
                    if (r)
                    {
                        double p = (t % 0.75) / 0.75;
                        double k = p < 0.35 ? p / 0.35 : p < 0.65 ? 1 : (1 - p) / 0.35;
                        Rot(rig.LegR, -95 * k * k * (3 - 2 * k));
                        double punch = 57.5 - 37.5 * Math.Cos(2 * Math.PI * t / 0.375);
                        Rot(rig.ArmL, punch); Rot(rig.ArmR, -punch);
                    }
                    else { rot = -7 * Math.Cos(2 * Math.PI * t / 0.5); }
                    break;
                case "tinker":
                    if (r) Rot(rig.ArmR, -55 - 15 * Sn(t, 0.3));
                    else rot = -7 * Math.Cos(2 * Math.PI * t / 0.5);
                    break;
                case "spin":
                    ax = Math.Cos(2 * Math.PI * t / 0.45);
                    break;
                case "sniff":
                    if (r)
                    {
                        if (rig.Head != null) rig.Head.R.Angle = 5 + 5 * Sn(t, 0.25);
                        Rot(rig.LegL, 8 * Sn(t, 0.6)); Rot(rig.LegR, -8 * Sn(t, 0.6));
                    }
                    break;
                case "highfive":
                    if (r) Rot(rig.ArmR, -135);
                    break;
                case "hug":
                    if (r) { Rot(rig.ArmL, -40); Rot(rig.ArmR, 40); }
                    break;
                case "pat":
                    if (r) Rot(rig.ArmR, -110 + 10 * Sn(t, 0.3));
                    break;
                case "pet":
                    if (r) Rot(rig.ArmR, -40 + 10 * Sn(t, 0.3));
                    break;
            }
            if (r && rig.Tail != null) rig.Tail.R.Angle = state == "sleep" ? 0 : 22 * Sn(t, happy ? 0.14 : 0.55);

            // one-shot effects
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                var fx = effects[i];
                double p = (World.Now - fx.Start) / fx.Dur;
                if (p >= 1) { effects.RemoveAt(i); continue; }
                if (fx.Kind == "hop")
                {
                    double[] keys = { 0, 0.15, 0.45, 0.8, 1 };
                    double[] hy = { 0, 0, -0.32, 0, 0 };
                    double[] hsx = { 1, 1.08, 0.95, 1.06, 1 };
                    double[] hsy = { 1, 0.9, 1.06, 0.93, 1 };
                    int k = 0;
                    while (k < keys.Length - 2 && p > keys[k + 1]) k++;
                    double f = (p - keys[k]) / (keys[k + 1] - keys[k]);
                    ty += VbH * (hy[k] + (hy[k + 1] - hy[k]) * f);
                    ax *= hsx[k] + (hsx[k + 1] - hsx[k]) * f;
                    ay *= hsy[k] + (hsy[k + 1] - hsy[k]) * f;
                }
                else if (fx.Kind == "squash")
                {
                    double sx = p < 0.5 ? 1.15 + (0.96 - 1.15) * (p / 0.5) : 0.96 + (1 - 0.96) * ((p - 0.5) / 0.5);
                    double sy = p < 0.5 ? 0.82 + (1.05 - 0.82) * (p / 0.5) : 1.05 + (1 - 1.05) * ((p - 0.5) / 0.5);
                    ax *= sx;
                    ay *= sy;
                }
                else if (fx.Kind == "shake")
                {
                    tx += 0.03 * VbW * Math.Sign(Math.Sin(2 * Math.PI * (World.Now - fx.Start) / 0.14));
                }
            }
        }

        double LegsTop()
        {
            // Top of the legs group = bottom pivot minus its height; derived from the parts.
            double top = double.MaxValue;
            foreach (var p in rig.AllLegs) top = Math.Min(top, p.R.CenterY);
            return top == double.MaxValue ? rig.Legs.S.CenterY : top;
        }

        // ----- per-frame placement -----
        public void Place(double x, double y, double facing, double tilt, bool held, bool hovering, bool happy, bool dog, string state)
        {
            double ax, ay, rot, tx, ty, px, py;
            Pose(state, World.Now, happy, dog, out ax, out ay, out rot, out tx, out ty, out px, out py);

            var mtx = Matrix.Identity;
            mtx.ScaleAt(ax, ay, px, py);
            mtx.RotateAt(rot, px, py);
            mtx.Translate(tx, ty);
            mtx.Translate(-VbW / 2, -VbH);
            mtx.Scale(s * facing, s);
            mtx.RotateAt(tilt, 0, held ? -m.H * 0.88 : 0);
            mtx.Translate(WinW / 2, WinH - FootPad);
            bodyTf.Matrix = mtx;

            double left = x - WinW / 2;
            double top = y - WinH + FootPad;
            if (Math.Abs(Left - left) > 0.05) Left = left;
            if (Math.Abs(Top - top) > 0.05) Top = top;

            double headTop = WinH - FootPad - m.H;
            double cx = WinW / 2;
            var wa = World.WA;

            if (faceEmoji.Visibility == Visibility.Visible)
            {
                Canvas.SetLeft(faceEmoji, cx + m.W / 2 - 12);
                Canvas.SetTop(faceEmoji, headTop - 6);
            }

            // emote bubble: up and to the right of the head
            if (emoteB.Visibility == Visibility.Visible)
            {
                if (World.Now > emoteUntil) emoteB.Visibility = Visibility.Collapsed;
                else
                {
                    Canvas.SetLeft(emoteB, cx + m.W * 0.25);
                    Canvas.SetTop(emoteB, headTop - 22);
                }
            }

            // speech bubble: above the head, kept on screen
            bool sayOn = sayB.Visibility == Visibility.Visible;
            if (sayOn && World.Now > sayUntil) { sayB.Visibility = Visibility.Collapsed; sayOn = false; }
            sayTail.Visibility = Visibility.Collapsed;
            if (sayOn)
            {
                double bw = sayB.DesiredSize.Width, bh = sayB.DesiredSize.Height;
                double bTop = headTop - bh - 10;
                if (top + bTop < wa.Top + 2)
                {
                    // no room above: put it beside the character
                    bool right = x < wa.Left + wa.Width / 2;
                    Canvas.SetLeft(sayB, right ? cx + m.W / 2 + 8 : cx - m.W / 2 - 8 - bw);
                    Canvas.SetTop(sayB, headTop + 2);
                }
                else
                {
                    double bLeft = cx - bw / 2;
                    double screenL = left + bLeft;
                    double dx = Math.Max(wa.Left + 4 - screenL, Math.Min(0, wa.Right - 4 - (screenL + bw)));
                    Canvas.SetLeft(sayB, bLeft + dx);
                    Canvas.SetTop(sayB, bTop);
                    sayTail.Visibility = Visibility.Visible;
                    Canvas.SetLeft(sayTail, cx - 6);
                    Canvas.SetTop(sayTail, bTop + bh - 1);
                }
            }

            // name tag when hovered
            bool showTag = hovering && !held && !sayOn;
            tagB.Visibility = showTag ? Visibility.Visible : Visibility.Collapsed;
            if (showTag)
            {
                tagB.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(tagB, cx - tagB.DesiredSize.Width / 2);
                Canvas.SetTop(tagB, headTop - tagB.DesiredSize.Height - 6);
            }
        }
    }
}
