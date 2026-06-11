using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ContentDistributionPlayer.Controls
{
    class PictureBoxWithOpacity : PictureBox
    {
        private Bitmap _sourceImage;
        private Single _opacity = 1;

        private bool _isAnimatedGIF = false;
        private bool _avoidRedraw = false;
        private Image _originalImage;

        private Rectangle _drawRect;

        public void Initialize()
        {
            if (this.Image != null)
            {
                _originalImage = this.Image;

                //check if it's an animated gif
                FrameDimension dimension = new FrameDimension(this.Image.FrameDimensionsList[0]);
                // Number of frames
                int frameCount = this.Image.GetFrameCount(dimension);
                _isAnimatedGIF = (frameCount > 0);

                _sourceImage = new Bitmap(this.Image);
            }

            this.Image = null;
        }

        public Single Opacity
        {
            get
            {
                return _opacity;
            }

            set
            {
                _opacity = value;

                if (this.IsHandleCreated)
                {
                    this.Invoke(new Action(() =>
                    {
                        if (_originalImage != null)
                        {
                            if (_opacity >= 1 && !_avoidRedraw) // && _isAnimatedGIF)
                            {
                                // to start the gif animation I need to reset the image property
                                _avoidRedraw = true;
                                this.Image = _originalImage;
                            }
                            else
                            {
                                this.Image = null;
                                if (_avoidRedraw)
                                    _avoidRedraw = false;
                            }
                        }

                        this.Invalidate();
                    }));
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            if (_sourceImage != null)
            {
                var rW = (float)this.Width / (float)_sourceImage.Width;
                var rH = (float)this.Height / (float)_sourceImage.Height;
                var r = rH;
                if (rW < rH)
                    r = rW;

                var w = (int)(_sourceImage.Width * r);
                var h = (int)(_sourceImage.Height * r);

                // draw the image centered in the picturebox area
                _drawRect = new Rectangle((this.Width - w) / 2, (this.Height - h) / 2, w, h);
            }
        }

        //ColorMatrix middleColorMatrix = new ColorMatrix();
        //ImageAttributes middleImageAttributes = new ImageAttributes();

        protected override void OnPaint(PaintEventArgs pe)
        {
            base.OnPaint(pe);

            if (_sourceImage != null && !_avoidRedraw)
            {                
                using (Bitmap fadedImage = FadeBitmap(_sourceImage, _opacity))
                {
                    pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    pe.Graphics.DrawImage(fadedImage, _drawRect);
                }

                /*
                 * Optimized code doesn't work!
                 * 
                //Set alpha in the ColorMatrix
                middleColorMatrix.Matrix33 = _opacity;

                Debug.WriteLine(_opacity);

                // Set color matrix of imageAttributes
                middleImageAttributes.SetColorMatrix(middleColorMatrix,
                    ColorMatrixFlag.Default,
                    ColorAdjustType.Bitmap);

                // Draw the middle image
                PointF[] destpoints = new PointF[] { new Point(0, 0), new Point(_sourceImage.Width, 0), new Point(0, _sourceImage.Height) };
                pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                pe.Graphics.DrawImage(_sourceImage,
                    destpoints,
                    new Rectangle((this.Width - _drawImageWidth) / 2, (this.Height - _drawImageHeight) / 2, _drawImageWidth, _drawImageHeight),                    
                    GraphicsUnit.Pixel, middleImageAttributes);
                    */

            }
        }

        private Bitmap FadeBitmap(Bitmap bmp, Single opacity)
        {
            Bitmap bmp2 = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
            opacity = Math.Max(0, Math.Min(opacity, 1.0F));
            using (var ia = new ImageAttributes())
            {
                ColorMatrix cm = new ColorMatrix
                {
                    Matrix33 = opacity
                };
                ia.SetColorMatrix(cm);
                PointF[] destpoints = new PointF[] { new Point(0, 0), new Point(bmp.Width, 0), new Point(0, bmp.Height) };
                using (Graphics g = Graphics.FromImage(bmp2))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.DrawImage(bmp, destpoints, new RectangleF(Point.Empty, bmp.Size), GraphicsUnit.Pixel, ia);
                }
            }
            return bmp2;
        }

    }
}
