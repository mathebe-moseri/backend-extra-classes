//using Microsoft.AspNetCore.Mvc;
//using Microsoft.Extensions.Configuration;
//using System.Data;
//using System.Data.SqlClient;
//using System.Net.Http;
//using System.Net.Http.Headers;
//using System;
//using System.Threading.Tasks;

//namespace extra_classes.Controllers
//{
//    [ApiController]
//    public class extraClass : ControllerBase
//    {
//        private IConfiguration _configuration;

//        public extraClass(IConfiguration configuration)
//        {
//            _configuration = configuration;
//        }

//        // ---------------- GET endpoint to fetch all learners ----------------
//        [HttpGet("get_learnerInfo")]
//        public JsonResult get_learnerInfo()
//        {
//            string query = "SELECT * FROM ClassRegistrations";
//            DataTable table = new DataTable();
//            string SqlDataSource = _configuration.GetConnectionString("mathebe");

//            using (SqlConnection myCon = new SqlConnection(SqlDataSource))
//            {
//                myCon.Open();
//                using (SqlCommand myCommand = new SqlCommand(query, myCon))
//                {
//                    SqlDataReader myReader = myCommand.ExecuteReader();
//                    table.Load(myReader);
//                }
//            }
//            return new JsonResult(table);
//        }

//        // ---------------- POST endpoint to add learner ----------------
//        [HttpPost("add_learnerInfo")]
//        public async Task<JsonResult> addLearnerInfo([FromBody] ClassRegistration learner)
//        {
//            if (learner == null)
//                return new JsonResult(new { message = "Learner data is null" });

//            string query = @"
//                INSERT INTO ClassRegistrations
//                (LearnerFirstName, LearnerSurname, Grade, Email, SchoolName, ParentFullName, ParentCell)
//                VALUES
//                (@LearnerFirstName, @LearnerSurname, @Grade, @Email, @SchoolName, @ParentFullName, @ParentCell)";
//            string SqlDataSource = _configuration.GetConnectionString("mathebe");

//            using (SqlConnection conn = new SqlConnection(SqlDataSource))
//            {
//                conn.Open();
//                using (SqlCommand cmd = new SqlCommand(query, conn))
//                {
//                    cmd.Parameters.AddWithValue("@LearnerFirstName", learner.LearnerFirstName ?? string.Empty);
//                    cmd.Parameters.AddWithValue("@LearnerSurname", learner.LearnerSurname ?? string.Empty);
//                    cmd.Parameters.AddWithValue("@Grade", learner.Grade ?? string.Empty);
//                    cmd.Parameters.AddWithValue("@Email", learner.Email ?? string.Empty);
//                    cmd.Parameters.AddWithValue("@SchoolName", learner.SchoolName ?? string.Empty);
//                    cmd.Parameters.AddWithValue("@ParentFullName", learner.ParentFullName ?? string.Empty);
//                    cmd.Parameters.AddWithValue("@ParentCell", learner.ParentCell ?? string.Empty);
//                    cmd.ExecuteNonQuery();
//                }
//            }

//            // Send welcome email
//            await SendWelcomeEmail(learner.Email, learner.LearnerFirstName);

//            return new JsonResult(new { message = "Learner joined successfully, welcome email sent!" });
//        }

//        // ---------------- POST endpoint to send contact emails ----------------
//        [HttpPost("send_contact_email")]
//        public async Task<JsonResult> SendContactEmail([FromBody] ContactMessage message)
//        {
//            if (message == null || string.IsNullOrEmpty(message.Email) || string.IsNullOrEmpty(message.Message))
//                return new JsonResult(new { success = false, message = "Email or message cannot be empty." });

//            // Send email via Mailgun
//            await SendEmailViaMailgun(message.Email, message.Message);

//            return new JsonResult(new { success = true, message = "Your message has been sent successfully!" });
//        }

//        // ---------------- Mailgun welcome email ----------------
//        private async Task SendWelcomeEmail(string toEmail, string firstName)
//        {
//            string apiKey = "bc667a1c52d55177f003fd8ba879f2d2-77c6c375-8b41c475";
//            string sandboxDomain = "sandboxf4bdc869bc8b473d88176656227a6c3f.mailgun.org";
//            string baseUrl = "https://api.mailgun.net/v3";

//            using var client = new HttpClient();
//            var byteArray = System.Text.Encoding.ASCII.GetBytes($"api:{apiKey}");
//            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

//            string joinLink = "https://example.com/join?password=12345";

//            string emailText = $"Dear {firstName},\n\n" +
//                               $"Welcome to Sesi Mathebe Remote Extra Classes! We are thrilled to have you join our community of learners.\n\n" +
//                               $"To get started, please click the link below to access your classes:\n" +
//                               $"{joinLink}\n\n" +
//                               $"If you have any questions or need assistance, feel free to reply to this email.\n\n" +
//                               $"We look forward to supporting your learning journey.\n\n" +
//                               $"Best regards,\n" +
//                               $"The Sesi Mathebe Team";

//            var form = new MultipartFormDataContent
//            {
//                { new StringContent($"Sesi Mathebe <mailgun@{sandboxDomain}>"), "from" },
//                { new StringContent(toEmail), "to" },
//                { new StringContent("Welcome to Sesi Mathebe Remote Extra Classes!"), "subject" },
//                { new StringContent(emailText), "text" }
//            };

//            var response = await client.PostAsync($"{baseUrl}/{sandboxDomain}/messages", form);
//            var content = await response.Content.ReadAsStringAsync();
//            Console.WriteLine($"Mailgun Response: {response.StatusCode} - {content}");
//        }

//        // ---------------- Mailgun for contact form with reply-to ----------------
//        private async Task SendEmailViaMailgun(string userEmail, string messageBody)
//        {
//            string apiKey = "bc667a1c52d55177f003fd8ba879f2d2-77c6c375-8b41c475";
//            string sandboxDomain = "sandboxf4bdc869bc8b473d88176656227a6c3f.mailgun.org";
//            string baseUrl = "https://api.mailgun.net/v3";

//            using var client = new HttpClient();
//            var byteArray = System.Text.Encoding.ASCII.GetBytes($"api:{apiKey}");
//            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

//            var form = new MultipartFormDataContent
//    {
//        { new StringContent($"Sesi Mathebe <mailgun@{sandboxDomain}>"), "from" },
//        { new StringContent("mathebemoseri@gmail.com"), "to" }, // <-- Your email
//        { new StringContent("New Contact Form Message"), "subject" },
//        { new StringContent($"From: {userEmail}\n\nMessage:\n{messageBody}"), "text" },
//        { new StringContent(userEmail), "h:Reply-To" } // reply-to header set to user email
//    };

//            var response = await client.PostAsync($"{baseUrl}/{sandboxDomain}/messages", form);
//            var content = await response.Content.ReadAsStringAsync();
//            Console.WriteLine($"Mailgun Response: {response.StatusCode} - {content}");
//        }

//        // ---------------- Models ----------------
//        public class ClassRegistration
//        {
//            public string LearnerFirstName { get; set; }
//            public string LearnerSurname { get; set; }
//            public string Grade { get; set; }
//            public string Email { get; set; }
//            public string SchoolName { get; set; }
//            public string ParentFullName { get; set; }
//            public string ParentCell { get; set; }
//        }

//        public class ContactMessage
//        {
//            public string Email { get; set; }
//            public string Message { get; set; }
//        }











//        [HttpPost("send_contact_whatsapp")]
//        public async Task<JsonResult> SendWhatsappNotification([FromBody] WhatsappContact contact)
//        {
//            if (contact == null || string.IsNullOrEmpty(contact.WhatsappNumber))
//                return new JsonResult(new { success = false });

//            string body =
//                $"A user clicked SEND VIA WHATSAPP\n\n" +
//                $"WhatsApp Number: {contact.WhatsappNumber}\n\n" +
//                $"Message:\n{contact.Message}";

//            await SendEmailViaMailgun("whatsapp@contact.com", body);

//            return new JsonResult(new { success = true });
//        }

//        public class WhatsappContact
//        {
//            public string WhatsappNumber { get; set; }
//            public string Message { get; set; }
//        }
//    }






//}





using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MimeKit;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace extra_classes.Controllers
{
    [ApiController]
    public class extraClass : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public extraClass(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // ---------------- GET endpoint to fetch all learners ----------------
        [HttpGet("get_learnerInfo")]
        public JsonResult get_learnerInfo()
        {

            string query = "SELECT * FROM ClassRegistrations";
            DataTable table = new DataTable();
            string SqlDataSource = _configuration.GetConnectionString("mathebe");

            using (SqlConnection myCon = new SqlConnection(SqlDataSource))
            {
                myCon.Open();
                using (SqlCommand myCommand = new SqlCommand(query, myCon))
                using (SqlDataReader myReader = myCommand.ExecuteReader())
                {
                    table.Load(myReader);
                }
            }
            return new JsonResult(table);
        }

        // ---------------- POST endpoint to add learner ----------------
        [HttpPost("add_learnerInfo")]
        public async Task<JsonResult> addLearnerInfo([FromBody] ClassRegistration learner)
        {
            if (learner == null)
                return new JsonResult(new { success = false, message = "Learner data is null" });

            string query = @"
                INSERT INTO ClassRegistrations
                (LearnerFirstName, LearnerSurname, Grade, Email, SchoolName, ParentFullName, ParentCell)
                VALUES
                (@LearnerFirstName, @LearnerSurname, @Grade, @Email, @SchoolName, @ParentFullName, @ParentCell)";
            string SqlDataSource = _configuration.GetConnectionString("mathebe");

            using (SqlConnection conn = new SqlConnection(SqlDataSource))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@LearnerFirstName", learner.LearnerFirstName ?? string.Empty);
                    cmd.Parameters.AddWithValue("@LearnerSurname", learner.LearnerSurname ?? string.Empty);
                    cmd.Parameters.AddWithValue("@Grade", learner.Grade ?? string.Empty);
                    cmd.Parameters.AddWithValue("@Email", learner.Email ?? string.Empty);
                    cmd.Parameters.AddWithValue("@SchoolName", learner.SchoolName ?? string.Empty);
                    cmd.Parameters.AddWithValue("@ParentFullName", learner.ParentFullName ?? string.Empty);
                    cmd.Parameters.AddWithValue("@ParentCell", learner.ParentCell ?? string.Empty);
                    cmd.ExecuteNonQuery();
                }
            }

            // The learner is saved at this point. Now try the welcome email.
            bool emailSent = await SendWelcomeEmail(learner.Email, learner.LearnerFirstName);

            return new JsonResult(new
            {
                success = true,
                emailSent,
                message = emailSent
    ? "Registration received! Please check your email for the documents you need to send us."
    : "Registration received, but we could not send the confirmation email. Please contact us to complete your registration."
            });
        }

        // ---------------- POST endpoint to send contact emails ----------------
        [HttpPost("send_contact_email")]
        public async Task<JsonResult> SendContactEmail([FromBody] ContactMessage message)
        {
            if (message == null || string.IsNullOrWhiteSpace(message.Email) || string.IsNullOrWhiteSpace(message.Message))
                return new JsonResult(new { success = false, message = "Email or message cannot be empty." });

            bool sent = await SendContactToOwner(message.Email, message.Message);

            return new JsonResult(new
            {
                success = sent,
                message = sent
                    ? "Your message has been sent successfully!"
                    : "Sorry, your message could not be sent. Please try again later."
            });
        }

        // ---------------- POST endpoint for the WhatsApp contact notification ----------------
        [HttpPost("send_contact_whatsapp")]
        public async Task<JsonResult> SendWhatsappNotification([FromBody] WhatsappContact contact)
        {
            if (contact == null || string.IsNullOrWhiteSpace(contact.WhatsappNumber))
                return new JsonResult(new { success = false });

            string body =
                $"A user clicked SEND VIA WHATSAPP\n\n" +
                $"WhatsApp Number: {contact.WhatsappNumber}\n\n" +
                $"Message:\n{contact.Message}";

            // No reply-to here: the visitor gave a phone number, not an email address.
            bool sent = await SendEmail(OwnerAddress(), "New WhatsApp Contact", body);

            return new JsonResult(new { success = sent });
        }

        // ---------------- Email helpers (Gmail SMTP) ----------------
        private string OwnerAddress()
        {
            return _configuration["Gmail:Address"] ?? "sesimathebe.remote.extraclasses@gmail.com";
        }

        private async Task<bool> SendWelcomeEmail(string toEmail, string firstName)
        {
            string ownerEmail = OwnerAddress();
            string contractPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Documents", "learner_contract.pdf");

            string emailText = $"Dear {firstName},\n\n" +
                               $"Welcome to Sesi Mathebe Remote Extra Classes! Thank you for registering. We are thrilled to have you join our community of learners.\n\n" +
                               $"Please find the enrolment contract attached to this email. Kindly have the parent/guardian print, complete and sign it.\n\n" +
                               $"To complete your registration, please reply to this email and attach the following 3 documents:\n\n" +
                               $"1. Proof of payment\n" +
                               $"2. Your latest school report card\n" +
                               $"3. The signed contract (attached to this email)\n\n" +
                               $"Please note: you will only be allowed to log in and access your classes once your payment has been verified, " +
                               $"your signed contract has been received and your report card has been shared with us.\n\n" +
                               $"We will let you know as soon as everything has been checked and your access is activated.\n\n" +
                               $"If you have any questions or need assistance, feel free to reply to this email or contact us at {ownerEmail}.\n\n" +
                               $"We look forward to supporting your learning journey.\n\n" +
                               $"Best regards,\n" +
                               $"The Sesi Mathebe Team";

            return await SendEmail(
                toEmail,
                "Welcome to Sesi Mathebe - Documents Required to Complete Your Registration",
                emailText,
                null,
                contractPath);
        }

        private async Task<bool> SendContactToOwner(string userEmail, string messageBody)
        {
            string body = $"From: {userEmail}\n\nMessage:\n{messageBody}";
            return await SendEmail(OwnerAddress(), "New Contact Form Message", body, userEmail);
        }

        private async Task<bool> SendEmail(string to, string subject, string body, string replyTo = null, string attachmentPath = null)
        {
            try
            {
                string fromAddress = OwnerAddress();
                string appPassword = _configuration["Gmail:AppPassword"];

                if (string.IsNullOrEmpty(appPassword))
                {
                    Console.WriteLine("Email error: Gmail:AppPassword is not configured.");
                    return false;
                }

                if (!MailboxAddress.TryParse(to, out var toMailbox))
                {
                    Console.WriteLine($"Email error: invalid recipient address '{to}'.");
                    return false;
                }

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("Sesi Mathebe", fromAddress));
                message.To.Add(toMailbox);
                message.Subject = subject;

                if (!string.IsNullOrWhiteSpace(replyTo) && MailboxAddress.TryParse(replyTo, out var replyToMailbox))
                    message.ReplyTo.Add(replyToMailbox);

                var builder = new BodyBuilder { TextBody = body };

                if (!string.IsNullOrEmpty(attachmentPath))
                {
                    if (System.IO.File.Exists(attachmentPath))
                        builder.Attachments.Add(attachmentPath);
                    else
                        Console.WriteLine($"Email warning: attachment not found at '{attachmentPath}'.");
                }

                message.Body = builder.ToMessageBody();

                using var smtp = new SmtpClient();
                await smtp.ConnectAsync("smtp.gmail.com", 587, SecureSocketOptions.StartTls);
                await smtp.AuthenticateAsync(fromAddress, appPassword);
                await smtp.SendAsync(message);
                await smtp.DisconnectAsync(true);

                Console.WriteLine($"Email sent to {to}: {subject}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Email error: {ex.Message}");
                return false;
            }
        }

        // ---------------- Models ----------------
        public class ClassRegistration
        {
            public string LearnerFirstName { get; set; }
            public string LearnerSurname { get; set; }
            public string Grade { get; set; }
            public string Email { get; set; }
            public string SchoolName { get; set; }
            public string ParentFullName { get; set; }
            public string ParentCell { get; set; }
        }

        public class ContactMessage
        {
            public string Email { get; set; }
            public string Message { get; set; }
        }

        public class WhatsappContact
        {
            public string WhatsappNumber { get; set; }
            public string Message { get; set; }
        }
    }
}