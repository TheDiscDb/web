namespace TheDiscDb.Web.Barcode
{
    using System;
    using System.Linq;
    using SixLabors.Fonts;
    using SixLabors.ImageSharp;
    using SixLabors.ImageSharp.Drawing.Processing;
    using SixLabors.ImageSharp.PixelFormats;
    using SixLabors.ImageSharp.Processing;

    public class Barcode
    {
        public string Data { get; set; }
        public IBarcodeEncoder Encoder { get; set; } = new Code128Encoder();
        public Color ForegroundColor { get; set; } = Color.Black;
        public Color BackgroundColor { get; set; } = Color.White;
        public int Width { get; set; } = 300;
        public int Height { get; set; } = 150;
        public bool AutoSize { get; set; } = true;
        public bool ShowLabel { get; set; }
        public Font? LabelFont { get; set; }
        public LabelPosition LabelPosition { get; set; } = LabelPosition.BottomCenter;
        public AlignmentPosition AlignmentPosition { get; set; } = AlignmentPosition.Center;

        public Barcode(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
            {
                throw new ArgumentException($"'{nameof(data)}' cannot be null or whitespace.", nameof(data));
            }

            Data = data;
        }

        private Font? GetEffeciveFont()
        {
            if (!ShowLabel)
                return null;

            if (LabelFont != null)
                return LabelFont;

            var defaultFont = SystemFonts.Collection.Families.FirstOrDefault();

            return LabelFont = SystemFonts.CreateFont(defaultFont.Name, 10, FontStyle.Bold);
        }

        public Image GenerateImage()
        {
            if (string.IsNullOrWhiteSpace(Data))
            {
                throw new ArgumentException($"'{nameof(Data)}' cannot be null or whitespace.", nameof(Data));
            }

            const int barWidth = 2;
            const int aspectRatio = 2;

            var encodedData = this.Encoder.Encode(this.Data);

            if (Width == 0 || AutoSize)
            {
                Width = barWidth * encodedData.Length;
            }

            if (AutoSize)
            {
                Height = Width / aspectRatio;
            }

            float labelHeight = 0F, labelWidth = 0F;
            RichTextOptions? labelTextOptions = null;

            if (ShowLabel)
            {
                Font? font = GetEffeciveFont();
                if (font != null)
                {
                    labelTextOptions = new RichTextOptions(font)
                    {
                        Dpi = 200,
                    };

                    var labelSize = TextMeasurer.MeasureBounds(Data, labelTextOptions);
                    labelHeight = labelSize.Height;
                    labelWidth = labelSize.Width;
                }
            }

            var iBarWidth = Width / encodedData.Length;
            var shiftAdjustment = 0;
            var iBarWidthModifier = 1;

            switch (AlignmentPosition)
            {
                case AlignmentPosition.Center:
                    shiftAdjustment = (Width % encodedData.Length) / 2;
                    break;
                case AlignmentPosition.Left:
                    shiftAdjustment = 0;
                    break;
                case AlignmentPosition.Right:
                    shiftAdjustment = (Width % encodedData.Length);
                    break;
                default:
                    shiftAdjustment = (Width % encodedData.Length) / 2;
                    break;
            }

            if (iBarWidth <= 0)
                throw new Exception(
                    "EGENERATE_IMAGE-2: Image size specified not large enough to draw image. (Bar size determined to be less than 1 pixel)");

            //draw image
            var pos = 0;
            var halfBarWidth = (int)(iBarWidth * 0.5);

            var image = new Image<Rgba32>(Width, Height);
            image.Mutate(imageContext =>
            {
                //clears the image and colors the entire background
                imageContext.BackgroundColor(BackgroundColor);

                //lines are fBarWidth wide so draw the appropriate color line vertically
                var pen = Pens.Solid(ForegroundColor, iBarWidth / iBarWidthModifier);
                var drawingOptions = new DrawingOptions
                {
                    GraphicsOptions = new GraphicsOptions
                    {
                        Antialias = true,
                        AlphaCompositionMode = PixelAlphaCompositionMode.Src,
                    }
                };

                while (pos < encodedData.Length)
                {
                    if (encodedData[pos] == '1')
                    {
                        imageContext.DrawLine(drawingOptions, pen,
                            new PointF(pos * iBarWidth + shiftAdjustment + halfBarWidth, 0),
                            new PointF(pos * iBarWidth + shiftAdjustment + halfBarWidth, Height - labelHeight)
                        );
                    }

                    pos++;
                }
            });

            if (ShowLabel && labelTextOptions != null)
            {
                var labelY = 0;
                var labelX = 0;

                switch (LabelPosition)
                {
                    case LabelPosition.TopCenter:
                    case LabelPosition.BottomCenter:
                        labelY = image.Height - ((int)labelHeight);
                        labelX = Width / 2;
                        labelTextOptions.HorizontalAlignment = HorizontalAlignment.Center;
                        break;
                    case LabelPosition.TopLeft:
                    case LabelPosition.BottomLeft:
                        labelY = image.Height - ((int)labelHeight);
                        labelX = 0;
                        labelTextOptions.HorizontalAlignment = HorizontalAlignment.Left;
                        break;
                    case LabelPosition.TopRight:
                    case LabelPosition.BottomRight:
                        labelY = image.Height - ((int)labelHeight);
                        labelX = Width - (int)labelWidth;
                        labelTextOptions.HorizontalAlignment = HorizontalAlignment.Left;
                        break;
                }

                labelTextOptions.Origin = new Point(labelX, labelY);

                image.Mutate(x => x.DrawText(labelTextOptions, Data, ForegroundColor));
            }

            return image;
        }
    }
}
