using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ToastFish.View
{
    /// <summary>
    /// 默认渲染器把勾号按固定小尺寸画在勾选格左上角，菜单字号放大后勾号显得又小又偏。
    /// 这里改为在勾选格内按比例居中绘制。
    /// </summary>
    class MenuCheckRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color CheckColor = Color.FromArgb(32, 84, 148);

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
