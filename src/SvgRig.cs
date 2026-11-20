using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml;

namespace CovertCrew
{
    // A movable body part: scale + rotate around a pivot, then translate.
    class PartT
    {
        public ScaleTransform S = new ScaleTransform();
        public RotateTransform R = new RotateTransform();
        public TranslateTransform T = new TranslateTransform();

        public PartT(UIElement el, Point pivot)
        {
            S.CenterX = R.CenterX = pivot.X;
            S.CenterY = R.CenterY = pivot.Y;
            var g = new TransformGroup();
            g.Children.Add(S);
            g.Children.Add(R);
            g.Children.Add(T);
            el.RenderTransform = g;
        }

        public void Reset()
        {
            S.ScaleX = 1; S.ScaleY = 1; R.Angle = 0; T.X = 0; T.Y = 0;
        }
    }

    // Converts the character SVGs (a small, known subset) into WPF shapes,
    // keeping track of named parts so they can be animated.
    class SvgRig
    {
        public Canvas Root;
        public double VbW, VbH;
        public readonly Dictionary<string, List<UIElement>> ByClass = new Dictionary<string, List<UIElement>>();
        public readonly List<PartT> LegL = new List<PartT>(), LegR = new List<PartT>(), ArmL = new List<PartT>(), ArmR = new List<PartT>(), AllLegs = new List<PartT>();
        public PartT Legs, Upper, Tail, Head, Ear;
        readonly List<PartT> all = new List<PartT>();

        class Style
        {
            public Brush Fill = Brushes.Black;
            public Brush Stroke = null;
            public double StrokeWidth = 1;
            public PenLineCap Cap = PenLineCap.Flat;
            public PenLineJoin Join = PenLineJoin.Miter;
            public Style Clone() { return (Style)MemberwiseClone(); }
        }

        public static SvgRig Load(string svgText)
        {
            var doc = new XmlDocument();
            doc.LoadXml(svgText);
            var rig = new SvgRig();
            var svg = doc.DocumentElement;
            var vb = svg.GetAttribute("viewBox").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            rig.VbW = Num(vb[2]);
            rig.VbH = Num(vb[3]);
            rig.Root = new Canvas { Width = rig.VbW, Height = rig.VbH };
            Rect b;
            rig.BuildChildren(svg, rig.Root, new Style(), out b);
            return rig;
        }

        public void ResetParts()
        {
            foreach (var p in all) p.Reset();
        }

        public void Show(string cls, bool on)
        {
            List<UIElement> list;
            if (!ByClass.TryGetValue(cls, out list)) return;
            var v = on ? Visibility.Visible : Visibility.Collapsed;
            foreach (var el in list) if (el.Visibility != v) el.Visibility = v;
        }

        void BuildChildren(XmlElement parent, Canvas into, Style st, out Rect bounds)
        {
            bounds = Rect.Empty;
            foreach (XmlNode n in parent.ChildNodes)
            {
                var e = n as XmlElement;
                if (e == null) continue;
                Rect b;
                var el = Build(e, st, out b);
                if (el == null) continue;
                into.Children.Add(el);
                if (!b.IsEmpty) bounds.Union(b);
            }
        }

        UIElement Build(XmlElement e, Style parentStyle, out Rect bounds)
        {
            bounds = Rect.Empty;
            var st = parentStyle.Clone();
            ApplyStyle(e, st);
            UIElement el;
            string tag = e.LocalName;
            if (tag == "g")
            {
                var c = new Canvas();
                BuildChildren(e, c, st, out bounds);
                el = c;
            }
            else
            {
                Geometry geo = null;
                switch (tag)
                {
                    case "rect":
                        {
                            double rx = Num(e.GetAttribute("rx"));
                            double ry = e.HasAttribute("ry") ? Num(e.GetAttribute("ry")) : rx;
                            geo = new RectangleGeometry(new Rect(Num(e.GetAttribute("x")), Num(e.GetAttribute("y")), Num(e.GetAttribute("width")), Num(e.GetAttribute("height"))), rx, ry);
                            break;
                        }
                    case "circle":
                        {
                            double r = Num(e.GetAttribute("r"));
                            geo = new EllipseGeometry(new Point(Num(e.GetAttribute("cx")), Num(e.GetAttribute("cy"))), r, r);
                            break;
                        }
                    case "ellipse":
                        geo = new EllipseGeometry(new Point(Num(e.GetAttribute("cx")), Num(e.GetAttribute("cy"))), Num(e.GetAttribute("rx")), Num(e.GetAttribute("ry")));
                        break;
                    case "path":
                        geo = Geometry.Parse("F1 " + e.GetAttribute("d"));
                        break;
                    default:
                        return null;
                }
                var path = new Path
                {
                    Data = geo,
                    Fill = st.Fill,
                    Stroke = st.Stroke,
                    StrokeThickness = st.StrokeWidth,
                    StrokeStartLineCap = st.Cap,
                    StrokeEndLineCap = st.Cap,
                    StrokeLineJoin = st.Join,
                };
                bounds = geo.Bounds;
                el = path;
            }

            if (e.HasAttribute("opacity")) el.Opacity = Num(e.GetAttribute("opacity"));
            string tf = e.GetAttribute("transform");
            if (tf.StartsWith("rotate(", StringComparison.Ordinal))
            {
                var a = tf.Substring(7).TrimEnd(')').Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                el.RenderTransform = new RotateTransform(Num(a[0]), a.Length > 2 ? Num(a[1]) : 0, a.Length > 2 ? Num(a[2]) : 0);
            }

            string cls = e.GetAttribute("class");
            if (cls.Length > 0)
            {
                foreach (var c in cls.Split(' '))
                {
                    if (c.Length == 0) continue;
                    List<UIElement> list;
                    if (!ByClass.TryGetValue(c, out list)) ByClass[c] = list = new List<UIElement>();
                    list.Add(el);
                }
                RegisterPart(el, " " + cls + " ", bounds);
            }
            return el;
        }

        void RegisterPart(UIElement el, string cls, Rect b)
        {
            if (b.IsEmpty) return;
            PartT p = null;
            if (cls.Contains(" leg "))
            {
                p = new PartT(el, new Point(b.X + b.Width / 2, b.Top));
                AllLegs.Add(p);
                if (cls.Contains(" leg-l ")) LegL.Add(p); else LegR.Add(p);
            }
            else if (cls.Contains(" arm "))
            {
                p = new PartT(el, new Point(b.X + b.Width / 2, b.Top));
                if (cls.Contains(" arm-l ")) ArmL.Add(p); else ArmR.Add(p);
            }
            else if (cls.Contains(" legs ")) p = Legs = new PartT(el, new Point(b.X + b.Width / 2, b.Bottom));
            else if (cls.Contains(" upper ")) p = Upper = new PartT(el, new Point(b.X + b.Width / 2, b.Bottom));
            else if (cls.Contains(" tail ")) p = Tail = new PartT(el, new Point(b.Right, b.Bottom));
            else if (cls.Contains(" head ")) p = Head = new PartT(el, new Point(b.X + b.Width * 0.15, b.Y + b.Height * 0.85));
            else if (cls.Contains(" ear ")) p = Ear = new PartT(el, new Point(b.X + b.Width / 2, b.Y + b.Height * 0.1));
            if (p != null) all.Add(p);
        }

        static void ApplyStyle(XmlElement e, Style st)
        {
            if (e.HasAttribute("fill")) st.Fill = ParseBrush(e.GetAttribute("fill"));
            if (e.HasAttribute("stroke")) st.Stroke = ParseBrush(e.GetAttribute("stroke"));
            if (e.HasAttribute("stroke-width")) st.StrokeWidth = Num(e.GetAttribute("stroke-width"));
            string cap = e.GetAttribute("stroke-linecap");
            if (cap == "round") st.Cap = PenLineCap.Round;
            else if (cap == "square") st.Cap = PenLineCap.Square;
            string join = e.GetAttribute("stroke-linejoin");
            if (join == "round") st.Join = PenLineJoin.Round;
        }

        static readonly Dictionary<string, Brush> brushCache = new Dictionary<string, Brush>();

        static Brush ParseBrush(string s)
        {
            s = s.Trim();
            if (s == "none" || s.Length == 0) return null;
            Brush b;
            if (brushCache.TryGetValue(s, out b)) return b;
            Color c;
            if (s.StartsWith("rgba(", StringComparison.Ordinal))
            {
                var p = s.Substring(5).TrimEnd(')').Split(',');
                c = Color.FromArgb((byte)Math.Round(Num(p[3]) * 255), (byte)Num(p[0]), (byte)Num(p[1]), (byte)Num(p[2]));
            }
            else
            {
                c = (Color)ColorConverter.ConvertFromString(s);
            }
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            brushCache[s] = brush;
            return brush;
        }

        static double Num(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            double v;
            return double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0;
        }
    }
}
