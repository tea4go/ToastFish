using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ToastFish.View
{
    /// <summary>
    /// 默认渲染器把勾号按固定小尺寸画在勾选格左上角，菜单字号放大后勾号显得又小又偏；
    /// 左侧那层底色也按 16px 的老尺寸写死成 25px 宽，比勾选格还窄，勾号会露到白底上。
    /// 这里把勾号改为在勾选格内按比例居中绘制，底色加宽到与勾选列同宽。
    /// </summary>
    class MenuCheckRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color CheckColor = Color.FromArgb(32, 84, 148);

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // 勾选格左边缘固定距左 5px，即它居中在 ImageScalingSize + 9 宽的列里
            // （默认 16px 时正好是默认渲染器画的 25px，据此推得这个 9）。
            ToolStripDropDownMenu menu = e.ToolStrip as ToolStripDropDownMenu;
            if (menu != null)
            {
                Rectangle band = e.AffectedBounds;
                int column = menu.ImageScalingSize.Width + 9;
                if (column > band.Width)
                {
                    band.Width = column;
                    e = new ToolStripRenderEventArgs(e.Graphics, e.ToolStrip, band, e.BackColor);
                }
            }
            base.OnRenderImageMargin(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // 刻意不调 base：默认实现还会在勾选格上铺一块蓝色底，普通菜单没有这层底色
            Rectangle box = e.ImageRectangle;
            float side = Math.Min(box.Width, box.Height) * 0.58f;
            float left = box.Left + (box.Width - side) / 2f;
            float top = box.Top + (box.Height - side) / 2f;

            PointF[] points =
            {
                new PointF(left,                top + side * 0.50f),
                new PointF(left + side * 0.38f, top + side * 0.90f),
                new PointF(left + side,         top + side * 0.08f)
            };

            SmoothingMode saved = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(CheckColor, Math.Max(2f, side * 0.17f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                e.Graphics.DrawLines(pen, points);
            }
            e.Graphics.SmoothingMode = saved;
        }
    }
}
