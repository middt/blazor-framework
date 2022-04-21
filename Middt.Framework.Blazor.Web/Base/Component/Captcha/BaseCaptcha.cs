using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Middt.Framework.Blazor.Web.Base.Component.Captcha
{
    public class BaseCaptcha : ComponentBase
    {
        [Parameter]
        public EventCallback<string>? CaptchaWordChanged { get; set; }

        [Parameter]
        public int Width { get; set; } = 170;

        [Parameter]
        public int Height { get; set; } = 40;

        [Parameter]
        public int CharNumber { get; set; } = 4;

        protected string CaptchaWord;

        public string GetCaptchaWord(int length)
        {
            var random = new Random(DateTime.Now.Millisecond);

            const string chars = "ABCDEFGHIJKLMNOPRSTUQVTYZabcdefghijklmnoprstuqvyz0123456789";
            string cw = new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());

            return cw;
        }
        private async Task OnRefreshInternal()
        {
            CreateCaptcha();
            if (CaptchaWordChanged != null)
                await CaptchaWordChanged?.InvokeAsync(CaptchaWord);
        }
        protected void CreateCaptcha()
        {
            CaptchaWord = GetCaptchaWord(CharNumber);
            Initialization();
        }
        private Random RandomValue { get; set; }
        private List<Letter> Letters;
        private Color _BackgroundColor;

        public BaseCaptcha()
        {
            CreateCaptcha();
        }
        private void Initialization()
        {
            if (String.IsNullOrEmpty(CaptchaWord)) return;

            RandomValue = new Random();

            _BackgroundColor = Color.FromArgb(255, 253, 251);
            //Color.Gray;
            //Color.FromArgb(RandomValue.Next(100, 256), RandomValue.Next(100, 256), RandomValue.Next(100, 256)); ;

            var fontFamilies = new string[] { "Courier"/*, "Arial", "Verdana", "Times New Roman" */};

            Letters = new List<Letter>();

            if (!String.IsNullOrEmpty(CaptchaWord))
            {
                foreach (char c in CaptchaWord)
                {
                    var letter = new Letter
                    {
                        Value = c.ToString(),
                        //Angle = RandomValue.Next(-20, 20),
                        ForeColor = Color.Black,
                        //Color.FromArgb(RandomValue.Next(256), RandomValue.Next(256), RandomValue.Next(256)),
                        Family = fontFamilies[RandomValue.Next(0, fontFamilies.Length)],
                    };
                    letter.Font = new Font(letter.Family, RandomValue.Next(Height / 2, (Height / 2) + (Height / 4)), FontStyle.Bold);

                    Letters.Add(letter);
                }
            }

        }
        public bool IsValid(string CheckText)
        {
            if (!string.IsNullOrEmpty(CheckText) && CheckText.Equals(CaptchaWord))
            {
                return true;
            }
            return false;
        }
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (RandomValue == null) return;
            if (String.IsNullOrEmpty(CaptchaWord)) return;

            Bitmap bmp = new Bitmap(Width, Height);
            Graphics g = Graphics.FromImage(bmp);

            g.Clear(_BackgroundColor);

            var foreColor = Color.Gray;
            //var pen = new Pen(foreColor, 2.0f);

            float x = 2;

            foreach (Letter l in Letters)
            {
                SizeF textSize = g.MeasureString(l.Value, l.Font);

                var y = ((Height - textSize.Height) / 2);

                g.TranslateTransform(x, y);
                g.RotateTransform(l.Angle);
                g.DrawString(l.Value, l.Font, new SolidBrush(l.ForeColor), 0, 0);
                g.ResetTransform();

                x += textSize.Width;
            }

            foreColor = Color.FromArgb(RandomValue.Next(256), RandomValue.Next(256), RandomValue.Next(256));

            //pen = new Pen(foreColor, 0.8f);
            //g.DrawEllipse(pen, RandomValue.Next(-Width, Width), RandomValue.Next(-Height, Height), Width, Height);
            //g.DrawLine(pen, 0, RandomValue.Next(0, Height), Width, RandomValue.Next(0, Height));
            //g.DrawLine(pen, 0, RandomValue.Next(0, Height), Width, RandomValue.Next(0, Height));


            MemoryStream ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Gif);
            string imageBase64Data = Convert.ToBase64String(ms.ToArray());
            var img = string.Format("data:image/gif;base64,{0}", imageBase64Data);
            bmp.Dispose();


            var seq = 0;
            builder.OpenElement(++seq, "div");
            {
                builder.OpenElement(++seq, "img");
                builder.AddAttribute(++seq, "src", img);
                builder.CloseElement();

                builder.OpenElement(++seq, "button");
                {
                    builder.AddAttribute(++seq, "class", "btn btn-refresh");
                    //builder.AddAttribute(++seq, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, OnRefresh));
                    builder.AddAttribute(++seq, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () => OnRefreshInternal()));
                }
                builder.CloseElement();
            }
            builder.CloseElement();


            base.BuildRenderTree(builder);
        }

    }
}
