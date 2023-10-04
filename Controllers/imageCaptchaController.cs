using Microsoft.AspNetCore.Mvc;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace imageCaptcha.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CaptchaController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            Random rand = new Random();

            //Random sayı
            int num1 = rand.Next(1, 10), num2 = rand.Next(1, 10);
            string[] ops = { "+", "-", "*" };
            string op = ops[rand.Next(0, ops.Length)];

            //İşlem satırı
            string equation = $"{num1} {op} {num2} = ?";

            //Image oluşturma
            MemoryStream ms = new MemoryStream();
            using (Bitmap bmp = new Bitmap(200, 60))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.DrawString(equation, new Font("Arial", 24), Brushes.Black, new PointF(10, 10));
                }
                bmp.Save(ms, ImageFormat.Png);
            }

            //Doğru cevabı bulma
            double correctAnswer = op switch
            {
                "+" => num1 + num2,
                "-" => num1 - num2,
                "*" => num1 * num2,
            };

            //Doğru cevabı kaydetme
            HttpContext.Session.SetString("CaptchaAnswer", correctAnswer.ToString());


            return File(ms.ToArray(), "image/png");
        }
    }
}