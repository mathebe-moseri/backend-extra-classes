using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace extra_classes.Services
{
    public class EmailService
    {
        private readonly IConfiguration _config;
        public EmailService(IConfiguration config) { _config = config; }

        public async Task<bool> SendAsync(string to, string subject, string body)
        {
            try
            {
                string from = _config["Gmail:Address"] ?? "mathebemoseri@gmail.com";
                string? pass = _config["Gmail:AppPassword"];

                if (string.IsNullOrEmpty(pass))
                {
                    Console.WriteLine("Email error: Gmail:AppPassword is not configured.");
                    return false;
                }
                if (!MailboxAddress.TryParse(to, out var toBox))
                {
                    Console.WriteLine($"Email error: invalid recipient '{to}'.");
                    return false;
                }

                var msg = new MimeMessage();
                msg.From.Add(new MailboxAddress("Sesi Mathebe", from));
                msg.To.Add(toBox);
                msg.Subject = subject;
                msg.Body = new TextPart("plain") { Text = body };

                using var smtp = new SmtpClient();
                await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                await smtp.AuthenticateAsync(from, pass);
                await smtp.SendAsync(msg);
                await smtp.DisconnectAsync(true);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Email error: {ex.Message}");
                return false;
            }
        }
    }
}