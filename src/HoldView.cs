// SC Hauling Manager - C0P3Y | Icarus Interstellar Inc.
// 3D cargo hold view, drawn with GDI+ (no extra libraries): orthographic camera you can orbit (left-drag on empty
// space), zoom (wheel) and pan (middle-drag or Shift+drag). Containers can be dragged to any spot - they drop onto
// the floor or stack on top of other containers. R / right-click while dragging turns the container.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace HaulingManager
{
    public class HoldView : Control
    {
        public HoldModel Model;
        public Func<CargoBox, Color> ColorOf = b => Theme.Accent;
        public Func<CargoBox, string> Describe = b => b.Size + " SCU";
        public Func<CargoBox, string> DestShort = b => b.Dropoff;
        public Func<List<KeyValuePair<string, Color>>> Legend;
        public event EventHandler Changed;                       // a box was placed / moved / sent back
        public event Action<CargoBox> BoxClicked;
        public const string DragFormat = "SCHM_BOX_ID";

        double yaw = -0.75, pitch = 0.62, zoom = 1.0;
        PointF pan = PointF.Empty;
        double[] R, U, F, C;
        double scale = 10, cx, cy;

        enum Mode { None, Orbit, Pan, DragBox }
        Mode mode = Mode.None;
        Point last, downAt;
        CargoBox dragBox, hoverBox, extDragBox;
        bool dragRot;
        Placement ghost;
        bool ghostShown;
        public CargoBox Highlight;      // e.g. selected in the manifest list

        class Face
        {
            public PointF[] Pts;
            public double Depth;
            public Color Fill;
            public bool[] Edge;
            public CargoBox Box;
            public int Axis, Sign;
            public double[] Cell;    // world min corner of the unit cell this face belongs to
            public FaceFrame Frame;  // the whole box face this unit belongs to
            public float S0, T0;     // this unit's corner in the face frame (cells)
        }

        // A whole side of one crate, mapped to the screen with an affine matrix (orthographic camera), oriented so
        // text on it reads left-to-right and isn't mirrored. Decorations are drawn in its coordinates (1 = one cell).
        class FaceFrame
        {
            public float[] M;        // screen = (s,t) * [m0 m1; m2 m3] + (m4, m5)
            public float La, Lb;     // size along s and t in cells
            public bool Top, Side;
            public CargoBox Box;
            public Color Dest;
            public GraphicsPath Text;   // cached label path in frame coordinates (material + destination)
            public GraphicsPath SizeText;   // size number in the corner
            public bool TextBuilt;
            public int Ax, Sa, Bx, Sb;  // which world axes s/t run along, and their direction
        }
        List<Face> drawn = new List<Face>();

        public HoldView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Bg;
            AllowDrop = true;
            TabStop = true;
        }

        public void ResetView() { yaw = -0.75; pitch = 0.62; zoom = 1; pan = PointF.Empty; Invalidate(); }
        public void TurnView(double deg) { yaw += deg * Math.PI / 180.0; Invalidate(); }
        public void TopView() { pitch = 1.45; Invalidate(); }
        public void ZoomBy(double f) { zoom = Math.Max(0.2, Math.Min(12, zoom * f)); Invalidate(); }

        // ---------------------------------------------------------------- camera
        void SetupCamera()
        {
            double ct = Math.Cos(yaw), st = Math.Sin(yaw), cp = Math.Cos(pitch), sp = Math.Sin(pitch);
            R = new[] { ct, 0, -st };
            U = new[] { -st * sp, cp, -ct * sp };
            F = new[] { -st * cp, -sp, -ct * cp };
            double[] mn = { double.MaxValue, double.MaxValue, double.MaxValue }, mx = { double.MinValue, double.MinValue, double.MinValue };
            UseBay(null);
            foreach (BayInfo b in Model.Bays)
                foreach (double[] w in Corners(b))
                    for (int a = 0; a < 3; a++) { mn[a] = Math.Min(mn[a], w[a]); mx[a] = Math.Max(mx[a], w[a]); }
            if (Model.Bays.Count == 0) { mn = new double[] { 0, 0, 0 }; mx = new double[] { 1, 1, 1 }; }
            C = new[] { (mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2, (mn[2] + mx[2]) / 2 };
            // fit what's actually drawn (every bay corner + the orientation labels) into the view
            double pminx = double.MaxValue, pmaxx = double.MinValue, pminy = double.MaxValue, pmaxy = double.MinValue;
            Action<double, double, double> fit = (x, y, z) =>
            {
                double dx = x - C[0], dy = y - C[1], dz = z - C[2];
                double px = R[0] * dx + R[1] * dy + R[2] * dz, py = U[0] * dx + U[1] * dy + U[2] * dz;
                pminx = Math.Min(pminx, px); pmaxx = Math.Max(pmaxx, px); pminy = Math.Min(pminy, py); pmaxy = Math.Max(pmaxy, py);
            };
            foreach (BayInfo b in Model.Bays)
                foreach (double[] w in Corners(b)) fit(w[0], w[1], w[2]);
            double[] lmn, lmx; double loff;
            if (ShipBounds(out lmn, out lmx, out loff))
            {
                double mx0 = (lmn[0] + lmx[0]) / 2, mz0 = (lmn[2] + lmx[2]) / 2;
                fit(lmn[0] - loff - 1, lmn[1], mz0); fit(lmx[0] + loff + 1, lmn[1], mz0);
                fit(mx0, lmn[1], lmn[2] - loff - 1); fit(mx0, lmn[1], lmx[2] + loff + 1);
            }
            if (Model.Bays.Count == 0) { fit(0, 0, 0); fit(1, 1, 1); }
            double availW = Math.Max(50, Width - 20), availH = Math.Max(50, Height - 50);
            scale = Math.Min(availW / Math.Max(0.5, pmaxx - pminx), availH / Math.Max(0.5, pmaxy - pminy)) * 0.94 * zoom;
            // centre the projected box (not the 3D centre) so tall / long ships use the space
            cx = Width / 2.0 + pan.X - scale * (pminx + pmaxx) / 2;
            cy = (Height - 24) / 2.0 + pan.Y + scale * (pminy + pmaxy) / 2;
        }

        // the 8 corners of a bay where they really are in the ship (turned bays included)
        static IEnumerable<double[]> Corners(BayInfo b)
        {
            double[] o = b.Origin(); int[] s = b.Size();
            for (int i = 0; i < 8; i++)
                yield return b.ToWorld((i & 1) == 0 ? o[0] : o[0] + s[0], (i & 2) == 0 ? o[1] : o[1] + s[1], (i & 4) == 0 ? o[2] : o[2] + s[2]);
        }

        static double Sq(double v) { return v * v; }
        static double Dot(double[] a, double[] b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }

        // While drawing a turned bay (and its crates), points are given in the bay's unturned frame and turned here.
        double[] curM, curC;
        void UseBay(BayInfo b)
        {
            if (b != null && b.Rotated()) { curM = b.RotationMatrix(); curC = b.Center(); }
            else { curM = null; curC = null; }
        }
        void Turn(ref double x, ref double y, ref double z)
        {
            if (curM == null) return;
            double dx = x - curC[0], dy = y - curC[1], dz = z - curC[2];
            x = curC[0] + curM[0] * dx + curM[1] * dy + curM[2] * dz;
            y = curC[1] + curM[3] * dx + curM[4] * dy + curM[5] * dz;
            z = curC[2] + curM[6] * dx + curM[7] * dy + curM[8] * dz;
        }
        double[] TurnDir(double[] v)
        {
            if (curM == null) return v;
            return new[] { curM[0] * v[0] + curM[1] * v[1] + curM[2] * v[2], curM[3] * v[0] + curM[4] * v[1] + curM[5] * v[2], curM[6] * v[0] + curM[7] * v[1] + curM[8] * v[2] };
        }

        PointF Project(double x, double y, double z)
        {
            Turn(ref x, ref y, ref z);
            double dx = x - C[0], dy = y - C[1], dz = z - C[2];
            return new PointF((float)(cx + scale * (R[0] * dx + R[1] * dy + R[2] * dz)), (float)(cy - scale * (U[0] * dx + U[1] * dy + U[2] * dz)));
        }

        double DepthOf(double x, double y, double z)
        {
            Turn(ref x, ref y, ref z);
            return F[0] * (x - C[0]) + F[1] * (y - C[1]) + F[2] * (z - C[2]);
        }

        // ---------------------------------------------------------------- painting
        // Never let a drawing problem kill the control (WinForms would show a permanent red X).
        protected override void OnPaint(PaintEventArgs e)
        {
            try { PaintScene(e); }
            catch (Exception ex)
            {
                try
                {
                    e.Graphics.ResetTransform();
                    e.Graphics.Clear(Theme.Bg);
                    using (SolidBrush b = new SolidBrush(Theme.Highlight))
                        e.Graphics.DrawString("The 3D view hit a problem drawing this frame:\r\n" + ex.Message + "\r\n\r\nPress Reset, or rotate the view, to redraw.", Theme.Base, b, 16, 16);
                }
                catch { }
            }
        }

        void PaintScene(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            using (LinearGradientBrush bg = new LinearGradientBrush(ClientRectangle, Theme.HoldBg, Theme.Bg, LinearGradientMode.Vertical))
                g.FillRectangle(bg, ClientRectangle);
            if (Model == null || Model.Bays.Count == 0)
            {
                using (SolidBrush b = new SolidBrush(Theme.Muted))
                    g.DrawString("No ship selected.\r\nPick your ship in the Route Plan (Ship box) to see its cargo grid.", Theme.Semibold, b, 16, 16);
                return;
            }
            SetupCamera();
            g.SmoothingMode = SmoothingMode.AntiAlias;

            foreach (BayInfo bay in Model.Bays) DrawBay(g, bay);
            UseBay(null);
            DrawOrientation(g);

            // faces of every container on the ship, painted far to near
            List<Face> faces = new List<Face>();
            Dictionary<long, CargoBox> cells = new Dictionary<long, CargoBox>();
            List<CargoBox> boxes = Model.OnShip().Where(b => b != dragBox).ToList();
            foreach (CargoBox b in boxes)
            {
                BayInfo bay = Model.BayById(b.Bay); if (bay == null) continue;
                double[] o = bay.Origin(); int[] d = HoldModel.DimsIn(bay, b.Size, b.Rot);
                for (int x = 0; x < d[0]; x++) for (int y = 0; y < d[1]; y++) for (int z = 0; z < d[2]; z++)
                    cells[Key(o[0] + b.X + x, o[1] + b.Y + y, o[2] + b.Z + z)] = b;
            }
            foreach (CargoBox b in boxes) AddBoxFaces(faces, b, cells);
            UseBay(null);
            faces.Sort((a, b) => b.Depth.CompareTo(a.Depth));
            foreach (Face f in faces) DrawFace(g, f);
            drawn = faces;

            if (ghostShown && ghost.Bay != null)
                DrawGhost(g, ghost);
            UseBay(null);

            DrawOverlay(g);
        }

        // ---------------------------------------------------------------- ship orientation labels
        bool ShipBounds(out double[] mn, out double[] mx, out double off)
        {
            mn = new[] { double.MaxValue, double.MaxValue, double.MaxValue }; mx = new[] { double.MinValue, double.MinValue, double.MinValue };
            bool any = false;
            foreach (BayInfo b in Model.Bays)
            {
                if (b.OffGrid) continue;
                foreach (double[] w in Corners(b))
                    for (int a = 0; a < 3; a++) { mn[a] = Math.Min(mn[a], w[a]); mx[a] = Math.Max(mx[a], w[a]); }
                any = true;
            }
            off = any ? Math.Min(3.0, Math.Max(1.2, Math.Max(mx[0] - mn[0], mx[2] - mn[2]) * 0.08)) : 0;
            return any;
        }

        static string Opposite(string d) { return (d.Length == 2 ? d.Substring(0, 1) + (d[1] == '-' ? "+" : "-") : d); }

        void DrawOrientation(Graphics g)
        {
            double[] mn, mx; double off;
            if (!ShipBounds(out mn, out mx, out off)) return;
            string fore = Model.Ship != null && !string.IsNullOrEmpty(Model.Ship.Fore) ? Model.Ship.Fore : "z-";
            string stbd = Model.Ship != null && !string.IsNullOrEmpty(Model.Ship.Starboard) ? Model.Ship.Starboard
                        : fore == "z-" ? "x+" : fore == "z+" ? "x-" : fore == "x+" ? "z+" : "z-";
            double cxw = (mn[0] + mx[0]) / 2, czw = (mn[2] + mx[2]) / 2, fy = mn[1];
            float px = (float)Math.Max(7.5, Math.Min(13, scale * 0.55));
            using (Font f = new Font(Theme.Semibold.FontFamily, px, FontStyle.Bold))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(Theme.IsDark ? 150 : 190, Theme.Accent)))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                foreach (KeyValuePair<string, string> l in new[] {
                    new KeyValuePair<string, string>(fore, "FRONT"), new KeyValuePair<string, string>(Opposite(fore), "REAR"),
                    new KeyValuePair<string, string>(stbd, "STARBOARD"), new KeyValuePair<string, string>(Opposite(stbd), "PORT") })
                {
                    double x = cxw, z = czw;
                    switch (l.Key)
                    {
                        case "x+": x = mx[0] + off; break;
                        case "x-": x = mn[0] - off; break;
                        case "z+": z = mx[2] + off; break;
                        case "z-": z = mn[2] - off; break;
                        default: continue;
                    }
                    g.DrawString(l.Value, f, b, Project(x, fy, z), sf);
                }
            }
        }

        static long Key(double x, double y, double z)
        {
            long a = (long)Math.Round(x * 2) + 4096, b = (long)Math.Round(y * 2) + 4096, c = (long)Math.Round(z * 2) + 4096;
            return (a << 28) | (b << 14) | c;
        }

        void DrawBay(Graphics g, BayInfo bay)
        {
            UseBay(bay);
            double[] o = bay.Origin(); int[] s = bay.Size();
            int ga = bay.GravityAxis(), sign = bay.GravitySign();
            int a1, a2; HoldModel.Lateral(ga, out a1, out a2);
            double fv = o[ga] + (sign < 0 ? 0 : s[ga]);
            Func<double, double, PointF> P = (u, v) =>
            {
                double[] w = new double[3]; w[ga] = fv; w[a1] = o[a1] + u; w[a2] = o[a2] + v;
                return Project(w[0], w[1], w[2]);
            };
            Color floor = bay.OffGrid ? Color.FromArgb(35, Theme.Highlight) : bay.AutoLoad ? Color.FromArgb(Theme.IsDark ? 70 : 90, Theme.HoldFloor) : Color.FromArgb(30, Theme.Highlight);
            using (SolidBrush fb = new SolidBrush(floor))
                g.FillPolygon(fb, new[] { P(0, 0), P(s[a1], 0), P(s[a1], s[a2]), P(0, s[a2]) });
            using (Pen gp = new Pen(Color.FromArgb(Theme.IsDark ? 55 : 80, Theme.Accent), 1f))
            {
                for (int i = 0; i <= s[a1]; i++) g.DrawLine(gp, P(i, 0), P(i, s[a2]));
                for (int j = 0; j <= s[a2]; j++) g.DrawLine(gp, P(0, j), P(s[a1], j));
            }
            // outline of the bay volume
            using (Pen ep = new Pen(Color.FromArgb(bay.AutoLoad ? 110 : 90, bay.AutoLoad ? Theme.AccentDim : Theme.Highlight), 1.2f))
            {
                if (bay.OffGrid) ep.DashStyle = DashStyle.Dash;
                double x0 = o[0], y0 = o[1], z0 = o[2], x1 = o[0] + s[0], y1 = o[1] + s[1], z1 = o[2] + s[2];
                double[][] v = {
                    new[]{x0,y0,z0}, new[]{x1,y0,z0}, new[]{x1,y0,z1}, new[]{x0,y0,z1},
                    new[]{x0,y1,z0}, new[]{x1,y1,z0}, new[]{x1,y1,z1}, new[]{x0,y1,z1} };
                int[,] edges = { {0,1},{1,2},{2,3},{3,0},{4,5},{5,6},{6,7},{7,4},{0,4},{1,5},{2,6},{3,7} };
                for (int i = 0; i < 12; i++)
                {
                    double[] p = v[edges[i, 0]], q = v[edges[i, 1]];
                    g.DrawLine(ep, Project(p[0], p[1], p[2]), Project(q[0], q[1], q[2]));
                }
            }
            if (scale > 6)
            {
                PointF lp = P(0, 0);
                string name = bay.OffGrid ? "OFF GRID  -  drag crates here that aren't on the cargo grid"
                    : bay.Name + (bay.MaxSize > 0 ? "  (max " + bay.MaxSize + ")" : "") + (bay.AutoLoad ? "" : "  - not auto-filled");
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(170, Theme.Muted)))
                    g.DrawString(name, Theme.Small, tb, lp.X + 2, lp.Y + 2);
            }
        }

        static Color CrateBody { get { return Theme.CrateBody; } }
        static Color CrateFrame { get { return Theme.CrateFrame; } }
        static Color CrateRib { get { return Theme.Mix(Theme.CrateBody, Color.Black, 0.35f); } }
        static Color CrateText { get { return Theme.CrateText; } }
        static FontFamily labelFont;
        static FontFamily LabelFont
        {
            get
            {
                if (labelFont == null)
                {
                    try { labelFont = new FontFamily("Segoe UI Semibold"); }
                    catch { try { labelFont = new FontFamily("Segoe UI"); } catch { labelFont = FontFamily.GenericSansSerif; } }
                }
                return labelFont;
            }
        }
        bool fastDraw;   // while orbiting / panning: skip the text for speed

        PointF AxisVec(int axis)
        {
            double[] e = new double[3]; e[axis] = 1; e = TurnDir(e);
            return new PointF((float)(scale * Dot(R, e)), (float)(-scale * Dot(U, e)));
        }

        FaceFrame MakeFrame(CargoBox b, int axis, int sign, double[] lo, int[] d, Color dest)
        {
            int p = axis == 0 ? 1 : 0, q = axis == 2 ? 1 : 2;     // the two in-plane axes
            int ax, bx;
            bool side = axis != 1;
            if (side) { ax = p == 1 ? q : p; bx = 1; }           // across = the horizontal axis, down = y
            else
            {
                PointF vp = AxisVec(p), vq = AxisVec(q);
                if (d[p] != d[q]) { ax = d[p] > d[q] ? p : q; }
                else ax = Math.Abs(vp.X) / (Math.Abs(vp.Y) + 1e-6) >= Math.Abs(vq.X) / (Math.Abs(vq.Y) + 1e-6) ? p : q;
                bx = ax == p ? q : p;
            }
            PointF A = AxisVec(ax), B = AxisVec(bx);
            int sa = A.X > 0.001f || (Math.Abs(A.X) <= 0.001f && A.Y > 0) ? 1 : -1;
            A = new PointF(A.X * sa, A.Y * sa);
            int sb;
            if (side) sb = B.Y >= 0 ? 1 : -1;
            else sb = (A.X * B.Y - A.Y * B.X) >= 0 ? 1 : -1;
            B = new PointF(B.X * sb, B.Y * sb);
            double[] start = (double[])lo.Clone();
            start[axis] = lo[axis] + (sign > 0 ? d[axis] : 0);
            if (sa < 0) start[ax] = lo[ax] + d[ax];
            if (sb < 0) start[bx] = lo[bx] + d[bx];
            PointF O = Project(start[0], start[1], start[2]);
            FaceFrame fr = new FaceFrame
            {
                M = new[] { A.X, A.Y, B.X, B.Y, O.X, O.Y }, La = d[ax], Lb = d[bx], Top = axis == 1 && sign > 0, Side = side, Box = b, Dest = dest
            };
            fr.Ax = ax; fr.Sa = sa; fr.Bx = bx; fr.Sb = sb;
            return fr;
        }

        void AddBoxFaces(List<Face> faces, CargoBox b, Dictionary<long, CargoBox> cells)
        {
            BayInfo bay = Model.BayById(b.Bay); if (bay == null) return;
            UseBay(bay);
            double[] o = bay.Origin(); int[] d = HoldModel.DimsIn(bay, b.Size, b.Rot);
            double bx = o[0] + b.X, by = o[1] + b.Y, bz = o[2] + b.Z;
            double[] lo = { bx, by, bz };
            Color dest = ColorOf(b);
            bool hl = b == hoverBox || b == Highlight;
            Color body = hl ? Lighten(CrateBody, 0.25) : CrateBody;
            for (int axis = 0; axis < 3; axis++)
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    double[] n = new double[3]; n[axis] = sign;
                    if (Dot(TurnDir(n), F) >= -1e-9) continue;   // facing away from the camera
                    int ua = axis == 0 ? 1 : 0, va = axis == 2 ? 1 : 2;
                    int layer = sign > 0 ? d[axis] - 1 : 0;
                    double shade = axis == 1 ? (sign > 0 ? 1.18 : 0.7) : (axis == 0 ? 1.0 : 0.88);
                    Color fill = Shade(body, shade);
                    FaceFrame frame = MakeFrame(b, axis, sign, lo, d, dest);
                    for (int i = 0; i < d[ua]; i++)
                        for (int j = 0; j < d[va]; j++)
                        {
                            double[] cell = new double[3];
                            cell[0] = bx; cell[1] = by; cell[2] = bz;
                            cell[axis] += layer; cell[ua] += i; cell[va] += j;
                            double[] nb = (double[])cell.Clone(); nb[axis] += sign;
                            CargoBox other;
                            if (cells.TryGetValue(Key(nb[0], nb[1], nb[2]), out other) && other != b && other.Bay == b.Bay) continue;   // hidden behind a neighbour
                            double plane = cell[axis] + (sign > 0 ? 1 : 0);
                            double[] uv = { 0, 1, 1, 0 }, vv = { 0, 0, 1, 1 };
                            PointF[] pts = new PointF[4];
                            double cxw = 0, cyw = 0, czw = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                double[] w = new double[3];
                                w[axis] = plane; w[ua] = cell[ua] + uv[k]; w[va] = cell[va] + vv[k];
                                pts[k] = Project(w[0], w[1], w[2]);
                                cxw += w[0] / 4; cyw += w[1] / 4; czw += w[2] / 4;
                            }
                            // where this unit sits in the face frame
                            int ia = (int)Math.Round(cell[frame.Ax] - lo[frame.Ax]), ib = (int)Math.Round(cell[frame.Bx] - lo[frame.Bx]);
                            float s0 = frame.Sa > 0 ? ia : frame.La - ia - 1, t0 = frame.Sb > 0 ? ib : frame.Lb - ib - 1;
                            faces.Add(new Face
                            {
                                Pts = pts, Depth = DepthOf(cxw, cyw, czw), Fill = fill, Box = b, Axis = axis, Sign = sign, Cell = cell,
                                Edge = new[] { j == 0, i == d[ua] - 1, j == d[va] - 1, i == 0 }, Frame = frame, S0 = s0, T0 = t0
                            });
                        }
                }
        }

        void DrawFace(Graphics g, Face f)
        {
            using (SolidBrush b = new SolidBrush(f.Fill)) g.FillPolygon(b, f.Pts);
            FaceFrame fr = f.Frame;
            float det = fr.M[0] * fr.M[3] - fr.M[1] * fr.M[2];
            if (Math.Abs(det) > 2f) DrawCrateDetail(g, f, fr);
            bool hl = f.Box == hoverBox || f.Box == Highlight;
            using (Pen p = new Pen(hl ? Color.White : Color.FromArgb(220, Theme.Mix(Theme.CrateBody, Color.Black, 0.8f)), hl ? 1.8f : 1.2f))
                for (int k = 0; k < 4; k++) if (f.Edge[k]) g.DrawLine(p, f.Pts[k], f.Pts[(k + 1) % 4]);
        }

        static PointF ToScreen(FaceFrame fr, float x, float y)
        {
            return new PointF(x * fr.M[0] + y * fr.M[2] + fr.M[4], x * fr.M[1] + y * fr.M[3] + fr.M[5]);
        }

        // Fills a rectangle given in face coordinates, cut to this unit square (no GDI clipping regions needed).
        static void FillPart(Graphics g, Face f, FaceFrame fr, Brush br, float x, float y, float w, float h)
        {
            float x0 = Math.Max(x, f.S0), y0 = Math.Max(y, f.T0), x1 = Math.Min(x + w, f.S0 + 1), y1 = Math.Min(y + h, f.T0 + 1);
            if (x1 <= x0 || y1 <= y0) return;
            g.FillPolygon(br, new[] { ToScreen(fr, x0, y0), ToScreen(fr, x1, y0), ToScreen(fr, x1, y1), ToScreen(fr, x0, y1) });
        }

        // Text is the one thing that needs a clip: it's drawn through the face matrix, clipped to this unit.
        static void FillText(Graphics g, Face f, FaceFrame fr, Brush br, GraphicsPath text)
        {
            GraphicsState st = g.Save();
            using (GraphicsPath clip = new GraphicsPath())
            {
                clip.AddPolygon(f.Pts);
                g.SetClip(clip, CombineMode.Intersect);
                g.MultiplyTransform(new Matrix(fr.M[0], fr.M[1], fr.M[2], fr.M[3], fr.M[4], fr.M[5]));
                g.FillPath(br, text);
            }
            g.Restore(st);
        }

        static bool Hits(Face f, float x, float y, float w, float h)
        {
            return x < f.S0 + 1 && x + w > f.S0 && y < f.T0 + 1 && y + h > f.T0;
        }

        // Crate look: very dark grey body, frame rails, ribbed sides and destination-colour corner brackets.
        // Every visible face carries the same label: size (top-left), cargo (centre), "-> destination" (bottom).
        void DrawCrateDetail(Graphics g, Face f, FaceFrame fr)
        {
            float La = fr.La, Lb = fr.Lb;
            float px = Math.Max(1f / (float)scale, 0.01f);      // one screen pixel in face units
            using (SolidBrush railB = new SolidBrush(Color.FromArgb(210, CrateFrame)))
            {
                float r = Math.Max(0.06f, px * 1.3f), o = 0.03f;
                FillPart(g, f, fr, railB, o, o, La - 2 * o, r);
                FillPart(g, f, fr, railB, o, Lb - o - r, La - 2 * o, r);
                FillPart(g, f, fr, railB, o, o, r, Lb - 2 * o);
                FillPart(g, f, fr, railB, La - o - r, o, r, Lb - 2 * o);
            }
            if (fr.Side)
            {
                float rw = Math.Max(0.03f, px);
                using (SolidBrush rb = new SolidBrush(Color.FromArgb(160, CrateRib)))
                    for (float x = 0.5f; x < La - 0.2f; x += 0.5f) FillPart(g, f, fr, rb, x - rw / 2, 0.14f, rw, Lb - 0.28f);
            }
            // corner brackets in the destination colour
            float len = Math.Min(0.42f, Math.Min(La, Lb) * 0.32f), th = Math.Max(0.09f, px * 2f);
            using (SolidBrush cb = new SolidBrush(fr.Dest))
            {
                FillPart(g, f, fr, cb, 0, 0, len, th); FillPart(g, f, fr, cb, 0, 0, th, len);
                FillPart(g, f, fr, cb, La - len, 0, len, th); FillPart(g, f, fr, cb, La - th, 0, th, len);
                FillPart(g, f, fr, cb, 0, Lb - th, len, th); FillPart(g, f, fr, cb, 0, Lb - len, th, len);
                FillPart(g, f, fr, cb, La - len, Lb - th, len, th); FillPart(g, f, fr, cb, La - th, Lb - len, th, len);
            }
            if (fastDraw) return;
            if (!fr.TextBuilt) { BuildFaceText(fr); fr.TextBuilt = true; }
            if (fr.SizeText != null)
                using (SolidBrush sb = new SolidBrush(fr.Dest)) FillText(g, f, fr, sb, fr.SizeText);
            if (fr.Text != null)
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(235, CrateText))) FillText(g, f, fr, tb, fr.Text);
        }

        // Builds the label paths for one face in its own coordinates. Three stacked rows that never overlap:
        // "<size> SCU" (top-left), cargo (middle) and "-> destination" (bottom), all scaled to the face size.
        // Pieces too small to read are left out.
        void BuildFaceText(FaceFrame fr)
        {
            float La = fr.La, Lb = fr.Lb;
            float minH = 7f / (float)scale;                       // ~7 px tall at the current zoom
            float inset = Math.Min(0.42f, Math.Min(La, Lb) * 0.32f) * 0.35f + 0.08f;
            StringFormat sf = StringFormat.GenericTypographic;
            float x0 = inset, wAv = La - 2 * inset;
            float top = inset, bottom = Lb - inset, hAv = bottom - top;
            if (wAv <= 0 || hAv <= 0) return;

            float hs = Math.Min(0.30f, hAv * 0.24f);              // size row
            float hd = Math.Min(0.26f, hAv * 0.19f);              // destination row
            float gap = Math.Max(hAv * 0.06f, 0.02f);
            float midTop = top + hs + gap, midBot = bottom - hd - gap;
            float hm = Math.Min(0.42f, midBot - midTop);          // cargo row

            if (hs >= minH)
            {
                GraphicsPath p = SizeLabel(fr.Box.Size);
                fr.SizeText = FitPath(p, new RectangleF(x0, top, Math.Min(wAv, La * 0.6f), hs), false);
            }

            GraphicsPath main = new GraphicsPath();
            bool any = false;
            if (hm >= minH)
            {
                GraphicsPath m = new GraphicsPath();
                m.AddString(fr.Box.Material ?? "", LabelFont, (int)FontStyle.Bold, 100f, PointF.Empty, sf);
                m = FitPath(m, new RectangleF(x0, midTop + (midBot - midTop - hm) / 2, wAv, hm), true);
                if (m != null) { main.AddPath(m, false); m.Dispose(); any = true; }
            }
            if (hd >= minH)
            {
                GraphicsPath d = new GraphicsPath();
                d.AddString("→ " + (DestShort(fr.Box) ?? "?"), LabelFont, (int)FontStyle.Regular, 100f, PointF.Empty, sf);
                d = FitPath(d, new RectangleF(x0, bottom - hd, wAv, hd), true);
                if (d != null) { main.AddPath(d, false); d.Dispose(); any = true; }
            }
            if (any) fr.Text = main; else main.Dispose();
        }

        // "<n> SCU": bold number with a smaller "SCU" after it, sharing the same baseline.
        static GraphicsPath SizeLabel(int size)
        {
            StringFormat sf = StringFormat.GenericTypographic;
            FontFamily ff = LabelFont;
            float asc = ff.GetCellAscent(FontStyle.Bold) / (float)ff.GetEmHeight(FontStyle.Bold);
            GraphicsPath p = new GraphicsPath();
            p.AddString(size.ToString(), ff, (int)FontStyle.Bold, 100f, PointF.Empty, sf);
            RectangleF r = p.GetBounds();
            float em2 = 62f;
            float asc2 = ff.GetCellAscent(FontStyle.Regular) / (float)ff.GetEmHeight(FontStyle.Regular);
            using (GraphicsPath u = new GraphicsPath())
            {
                u.AddString("SCU", ff, (int)FontStyle.Regular, em2, new PointF(r.Right + 14f, 100f * asc - em2 * asc2), sf);
                p.AddPath(u, false);
            }
            return p;
        }

        // Glyph outlines are built at a big size and then scaled down: tiny font sizes give broken outlines.
        static GraphicsPath FitPath(GraphicsPath p, RectangleF box, bool center)
        {
            RectangleF r = p.GetBounds();
            if (r.Width <= 0 || r.Height <= 0) { p.Dispose(); return null; }
            float k = Math.Min(box.Width / r.Width, box.Height / r.Height);
            using (Matrix m = new Matrix())
            {
                float dx = center ? (box.Width - r.Width * k) / 2 : 0;
                m.Translate(box.X + dx, box.Y + (box.Height - r.Height * k) / 2);
                m.Scale(k, k);
                m.Translate(-r.X, -r.Y);
                p.Transform(m);
            }
            return p;
        }

        void DrawGhost(Graphics g, Placement pl)
        {
            UseBay(pl.Bay);
            double[] o = pl.Bay.Origin();
            double x0 = o[0] + pl.P[0], y0 = o[1] + pl.P[1], z0 = o[2] + pl.P[2];
            double x1 = x0 + pl.D[0], y1 = y0 + pl.D[1], z1 = z0 + pl.D[2];
            Color c = pl.Valid ? Theme.Green : Theme.Red;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(90, c)))
            using (Pen p = new Pen(c, 2f))
            {
                for (int axis = 0; axis < 3; axis++)
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        double[] n = new double[3]; n[axis] = sign;
                        if (Dot(TurnDir(n), F) >= 0) continue;
                        double[] lo = { x0, y0, z0 }, hi = { x1, y1, z1 };
                        int ua = axis == 0 ? 1 : 0, va = axis == 2 ? 1 : 2;
                        double plane = sign > 0 ? hi[axis] : lo[axis];
                        PointF[] pts = new PointF[4];
                        double[] us = { lo[ua], hi[ua], hi[ua], lo[ua] }, vs = { lo[va], lo[va], hi[va], hi[va] };
                        for (int k = 0; k < 4; k++)
                        {
                            double[] w = new double[3]; w[axis] = plane; w[ua] = us[k]; w[va] = vs[k];
                            pts[k] = Project(w[0], w[1], w[2]);
                        }
                        g.FillPolygon(b, pts); g.DrawPolygon(p, pts);
                    }
            }
        }

        void DrawOverlay(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;
            if (Legend != null)
            {
                List<KeyValuePair<string, Color>> items = Legend();
                int max = Math.Min(items.Count, Math.Max(1, (Height - 60) / 17));
                float y = Height - 30 - max * 17;
                if (items.Count > 0)
                {
                    float w = 0;
                    for (int i = 0; i < max; i++) w = Math.Max(w, g.MeasureString(items[i].Key, Theme.Small).Width);
                    using (SolidBrush bb = new SolidBrush(Color.FromArgb(170, Theme.Header))) g.FillRectangle(bb, 6, y - 4, w + 30, max * 17 + 6);
                }
                for (int i = 0; i < max; i++)
                {
                    using (SolidBrush sb = new SolidBrush(items[i].Value)) g.FillRectangle(sb, 10, y + 3, 10, 10);
                    using (SolidBrush tb = new SolidBrush(Theme.Text)) g.DrawString(items[i].Key, Theme.Small, tb, 26, y);
                    y += 17;
                }
            }
            string hint = dragBox != null || extDragBox != null
                ? "Release to place  -  R or right-click turns it  -  Esc cancels"
                : "Drag a container to move it  -  drag empty space to rotate the view  -  wheel zooms  -  Shift+drag pans";
            using (SolidBrush hb = new SolidBrush(Color.FromArgb(160, Theme.Muted)))
                g.DrawString(hint, Theme.Small, hb, 10, Height - 22);
            CargoBox info = dragBox ?? extDragBox ?? hoverBox;
            if (info != null)
            {
                string t = Describe(info);
                SizeF sz = g.MeasureString(t, Theme.Semibold);
                RectangleF r = new RectangleF(Width - sz.Width - 26, Height - sz.Height - 34, sz.Width + 16, sz.Height + 10);
                using (SolidBrush bb = new SolidBrush(Color.FromArgb(225, Theme.Header))) g.FillRectangle(bb, r);
                using (Pen bp = new Pen(ColorOf(info))) g.DrawRectangle(bp, r.X, r.Y, r.Width, r.Height);
                using (SolidBrush tb = new SolidBrush(Theme.Text)) g.DrawString(t, Theme.Semibold, tb, r.X + 8, r.Y + 5);
            }
        }

        static Color Shade(Color c, double f)
        {
            return Color.FromArgb(c.A, (int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
        }

        static Color Lighten(Color c, double f)
        {
            return Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * f), (int)(c.G + (255 - c.G) * f), (int)(c.B + (255 - c.B) * f));
        }

        // ---------------------------------------------------------------- picking
        Face HitFace(Point pt)
        {
            for (int i = drawn.Count - 1; i >= 0; i--)
                if (Inside(drawn[i].Pts, pt)) return drawn[i];
            return null;
        }

        static bool Inside(PointF[] poly, Point p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if (((poly[i].Y > p.Y) != (poly[j].Y > p.Y)) &&
                    (p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)) inside = !inside;
            }
            return inside;
        }

        public CargoBox BoxAt(Point pt)
        {
            Face f = HitFace(pt);
            return f == null ? null : f.Box;
        }

        BayInfo BayContaining(double[] w, BayInfo prefer, out int[] local)
        {
            local = null;
            List<BayInfo> order = new List<BayInfo>();
            if (prefer != null) order.Add(prefer);
            order.AddRange(Model.Bays.Where(b => b != prefer && !b.Rotated()));   // a turned bay's frame only makes sense for its own crates
            foreach (BayInfo bay in order)
            {
                double[] o = bay.Origin(); int[] s = bay.Size();
                int[] l = new int[3]; bool ok = true;
                for (int a = 0; a < 3; a++)
                {
                    l[a] = (int)Math.Floor(w[a] - o[a] + 1e-6);
                    if (l[a] < 0 || l[a] >= s[a]) { ok = false; break; }
                }
                if (ok) { local = l; return bay; }
            }
            return null;
        }

        Placement ComputeGhost(Point pt, CargoBox box, bool rot)
        {
            SetupCamera();
            Face f = HitFace(pt);
            if (f != null && f.Box != box)
            {
                double[] target = (double[])f.Cell.Clone();
                target[f.Axis] += f.Sign;
                int[] local;
                BayInfo bay = BayContaining(new[] { target[0] + 0.5, target[1] + 0.5, target[2] + 0.5 }, Model.BayById(f.Box.Bay), out local);
                if (bay != null) return DropAt(bay, local, box, rot, false);
            }
            // otherwise: where the mouse ray meets a bay floor
            double xs = (pt.X - cx) / scale, us = (cy - pt.Y) / scale;
            double[] O = { C[0] + R[0] * xs + U[0] * us, C[1] + R[1] * xs + U[1] * us, C[2] + R[2] * xs + U[2] * us };
            BayInfo best = null; int[] bestLocal = null; double bestT = double.MaxValue;
            foreach (BayInfo bay in Model.Bays)
            {
                int ga = bay.GravityAxis(), sign = bay.GravitySign();
                double[] o = bay.Origin(); int[] s = bay.Size();
                // the mouse ray in this bay's unturned frame
                double[] Ob = O, Fb = F;
                if (bay.Rotated())
                {
                    double[] m = bay.RotationMatrix(), c = bay.Center();
                    double[] dO = { O[0] - c[0], O[1] - c[1], O[2] - c[2] };
                    Ob = new[] { c[0] + m[0] * dO[0] + m[3] * dO[1] + m[6] * dO[2], c[1] + m[1] * dO[0] + m[4] * dO[1] + m[7] * dO[2], c[2] + m[2] * dO[0] + m[5] * dO[1] + m[8] * dO[2] };
                    Fb = new[] { m[0] * F[0] + m[3] * F[1] + m[6] * F[2], m[1] * F[0] + m[4] * F[1] + m[7] * F[2], m[2] * F[0] + m[5] * F[1] + m[8] * F[2] };
                }
                if (Math.Abs(Fb[ga]) < 1e-6) continue;
                double v = o[ga] + (sign < 0 ? 0 : s[ga]);
                double t = (v - Ob[ga]) / Fb[ga];
                double[] w = { Ob[0] + Fb[0] * t, Ob[1] + Fb[1] * t, Ob[2] + Fb[2] * t };
                int a1, a2; HoldModel.Lateral(ga, out a1, out a2);
                double u = w[a1] - o[a1], vv = w[a2] - o[a2];
                if (u < 0 || vv < 0 || u >= s[a1] || vv >= s[a2]) continue;
                if (t < bestT)
                {
                    bestT = t; best = bay;
                    bestLocal = new int[3];
                    bestLocal[a1] = (int)Math.Floor(u); bestLocal[a2] = (int)Math.Floor(vv);
                    bestLocal[ga] = sign < 0 ? s[ga] - 1 : 0;   // start at the top, gravity brings it down
                }
            }
            if (best != null) return DropAt(best, bestLocal, box, rot, true);
            return new Placement();
        }

        Placement DropAt(BayInfo bay, int[] targetCell, CargoBox box, bool rot, bool fromTop)
        {
            int[] d = HoldModel.DimsIn(bay, box.Size, rot);
            int ga = bay.GravityAxis(), sign = bay.GravitySign();
            int a1, a2; HoldModel.Lateral(ga, out a1, out a2);
            int[] p = new int[3];
            p[a1] = targetCell[a1] - (d[a1] - 1) / 2;
            p[a2] = targetCell[a2] - (d[a2] - 1) / 2;
            if (fromTop) p[ga] = sign < 0 ? bay.Size()[ga] - d[ga] : 0;
            else p[ga] = sign < 0 ? targetCell[ga] : targetCell[ga] - d[ga] + 1;
            Placement pl = Model.Drop(bay, box.Size, rot, p, box);
            if (pl.D == null) pl.D = d;
            if (pl.P == null) pl.P = p;
            pl.Bay = bay;
            return pl;
        }

        // ---------------------------------------------------------------- mouse
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            last = e.Location; downAt = e.Location;
            if (Model == null || Model.Bays.Count == 0) return;
            if (mode == Mode.DragBox && e.Button == MouseButtons.Right) { dragRot = !dragRot; UpdateGhost(e.Location, dragBox); return; }
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && ModifierKeys == Keys.Shift)) { mode = Mode.Pan; return; }
            if (e.Button == MouseButtons.Left)
            {
                CargoBox b = BoxAt(e.Location);
                if (b != null)
                {
                    mode = Mode.DragBox; dragBox = b; dragRot = b.Rot;
                    if (BoxClicked != null) BoxClicked(b);
                    UpdateGhost(e.Location, b);
                    Capture = true;
                }
                else mode = Mode.Orbit;
            }
            else if (e.Button == MouseButtons.Right)
            {
                CargoBox b = BoxAt(e.Location);
                if (b != null) ShowBoxMenu(b, e.Location);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int dx = e.X - last.X, dy = e.Y - last.Y;
            last = e.Location;
            if (mode == Mode.Orbit) { fastDraw = true; yaw += dx * 0.01; pitch = Math.Max(0.12, Math.Min(1.5, pitch + dy * 0.008)); Invalidate(); return; }
            if (mode == Mode.Pan) { fastDraw = true; pan = new PointF(pan.X + dx, pan.Y + dy); Invalidate(); return; }
            if (mode == Mode.DragBox) { UpdateGhost(e.Location, dragBox); return; }
            CargoBox h = BoxAt(e.Location);
            if (h != hoverBox) { hoverBox = h; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (mode == Mode.DragBox && e.Button == MouseButtons.Left)
            {
                CargoBox b = dragBox;
                bool moved = Math.Abs(e.X - downAt.X) + Math.Abs(e.Y - downAt.Y) > 4;
                if (moved && ghostShown && ghost.Valid)
                {
                    Model.Place(b, ghost);
                    RaiseChanged();
                }
                EndDrag();
                Capture = false;
            }
            if (mode != Mode.DragBox) mode = Mode.None;
            if (fastDraw) { fastDraw = false; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hoverBox != null) { hoverBox = null; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ZoomBy(Math.Pow(1.12, e.Delta / 120.0));
        }

        protected override bool IsInputKey(Keys keyData) { return true; }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.R && (mode == Mode.DragBox || extDragBox != null))
            {
                dragRot = !dragRot;
                UpdateGhost(PointToClient(Cursor.Position), dragBox ?? extDragBox);
            }
            else if (e.KeyCode == Keys.Escape && mode == Mode.DragBox) { EndDrag(); Capture = false; }
            else if (e.KeyCode == Keys.Q) TurnView(-15);
            else if (e.KeyCode == Keys.E) TurnView(15);
        }

        void EndDrag()
        {
            dragBox = null; ghostShown = false; mode = Mode.None; Invalidate();
        }

        void UpdateGhost(Point pt, CargoBox b)
        {
            if (b == null) return;
            ghost = ComputeGhost(pt, b, dragRot);
            ghostShown = ghost.Bay != null;
            Invalidate();
        }

        void RaiseChanged() { if (Changed != null) Changed(this, EventArgs.Empty); }

        void ShowBoxMenu(CargoBox b, Point at)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors());
            m.BackColor = Theme.Header; m.ForeColor = Theme.Text;
            m.Items.Add("Turn 90°", null, delegate
            {
                BayInfo bay = Model.BayById(b.Bay);
                if (bay == null) return;
                Placement pl = Model.Drop(bay, b.Size, !b.Rot, new[] { b.X, b.Y, b.Z }, b);
                if (pl.Valid) { Model.Place(b, pl); RaiseChanged(); }
                else MessageBox.Show(this, "No room to turn it here - drag it somewhere with more space first.");
                Invalidate();
            });
            BayInfo off = Model.BayById(HoldModel.OffGridId);
            if (off != null && b.Bay != HoldModel.OffGridId)
                m.Items.Add("Move off grid", null, delegate
                {
                    if (Model.PlaceAnywhere(off, b)) RaiseChanged();
                    else MessageBox.Show(this, "The off-grid area is full.");
                    Invalidate();
                });
            m.Items.Add("Send back to the manifest", null, delegate { b.State = BoxState.Manifest; RaiseChanged(); Invalidate(); });
            foreach (ToolStripItem it in m.Items) it.ForeColor = Theme.Text;
            m.Show(this, at);
        }

        // ---------------------------------------------------------------- drag from the manifest list
        public Func<int, CargoBox> FindBox;

        protected override void OnDragEnter(DragEventArgs e)
        {
            extDragBox = ExtBox(e);
            e.Effect = extDragBox != null ? DragDropEffects.Move : DragDropEffects.None;
            if (extDragBox != null) dragRot = false;
        }

        protected override void OnDragOver(DragEventArgs e)
        {
            CargoBox b = ExtBox(e);
            if (b == null) { e.Effect = DragDropEffects.None; return; }
            extDragBox = b;
            UpdateGhost(PointToClient(new Point(e.X, e.Y)), b);
            e.Effect = ghost.Valid ? DragDropEffects.Move : DragDropEffects.None;
            if ((e.KeyState & 4) != 0 && !shiftLatch) { dragRot = !dragRot; shiftLatch = true; }   // Shift turns it while dragging from the list
            if ((e.KeyState & 4) == 0) shiftLatch = false;
        }
        bool shiftLatch;

        protected override void OnDragLeave(EventArgs e)
        {
            extDragBox = null; ghostShown = false; Invalidate();
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            CargoBox b = ExtBox(e);
            if (b != null)
            {
                UpdateGhost(PointToClient(new Point(e.X, e.Y)), b);
                if (ghost.Valid) { Model.Place(b, ghost); RaiseChanged(); }
            }
            extDragBox = null; ghostShown = false; Invalidate();
        }

        CargoBox ExtBox(DragEventArgs e)
        {
            if (FindBox == null || !e.Data.GetDataPresent(DragFormat)) return null;
            object v = e.Data.GetData(DragFormat);
            int id; if (v == null || !int.TryParse(v.ToString(), out id)) return null;
            return FindBox(id);
        }
    }

    public class DarkMenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.Selection; } }
        public override Color MenuItemBorder { get { return Theme.AccentDim; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Header; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Header; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Header; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Header; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Selection; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Selection; } }
    }
}
