using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Net.Http;
using System.Net.Http.Headers;
using System;
using System.Threading.Tasks;

namespace extra_classes.Controllers
{
    [ApiController]
    public class extraClass : ControllerBase
    {
        private IConfiguration _configuration;

        public extraClass(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        // Existing GET endpoint
        [HttpGet("get_learnerInfo")]
        public JsonResult get_learnerInfo()
        {
            string query = "SELECT * FROM ClassRegistrations";
            DataTable table = new DataTable();
            string SqlDataSource = _configuration.GetConnectionString("extra-classes");

            using (SqlConnection myCon = new SqlConnection(SqlDataSource))
            {
                myCon.Open();
                using (SqlCommand myCommand = new SqlCommand(query, myCon))
                {
                    SqlDataReader myReader = myCommand.ExecuteReader();
                    table.Load(myReader);
                }
            }
            return new JsonResult(table);
        }

        // New POST endpoint for Join action
        [HttpPost("join_learner")]
        public async Task<JsonResult> joinLearner([FromBody] ClassRegistration learner)
        {
            if (learner == null)
                return new JsonResult(new { message = "Learner data is null" });

            // Save learner to database
            string query = @"
                INSERT INTO ClassRegistrations
                (LearnerFirstName, LearnerSurname, Grade, Email, SchoolName, ParentFullName, ParentCell)
                VALUES
                (@LearnerFirstName, @LearnerSurname, @Grade, @Email, @SchoolName, @ParentFullName, @ParentCell)";
            string SqlDataSource = _configuration.GetConnectionString("extra-classes");

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

            // Send personalized welcome email
            await SendWelcomeEmail(learner.Email, learner.LearnerFirstName);

            return new JsonResult(new { message = "Learner joined successfully, welcome email sent!" });
        }

        // Mailgun welcome email method
        // Mailgun welcome email method
        private async Task SendWelcomeEmail(string toEmail, string firstName)
        {
            string apiKey = "bc667a1c52d55177f003fd8ba879f2d2-77c6c375-8b41c475";
            string sandboxDomain = "sandboxf4bdc869bc8b473d88176656227a6c3f.mailgun.org";
            string baseUrl = "https://api.mailgun.net/v3";

            using var client = new HttpClient();
            var byteArray = System.Text.Encoding.ASCII.GetBytes($"api:{apiKey}");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            // Replace this with your real join link if needed
            string joinLink = "https://example.com/join?password=12345";

            // Professional and clear welcome message
            string emailText = $"Dear {firstName},\n\n" +
                               $"Welcome to Sesi Mathebe Remote Extra Classes! We are thrilled to have you join our community of learners.\n\n" +
                               $"To get started, please click the link below to access your classes:\n" +
                               $"{joinLink}\n\n" +
                               $"If you have any questions or need assistance, feel free to reply to this email.\n\n" +
                               $"We look forward to supporting your learning journey.\n\n" +
                               $"Best regards,\n" +
                               $"The Sesi Mathebe Team";

            var form = new MultipartFormDataContent
    {
        { new StringContent($"Sesi Mathebe <mailgun@{sandboxDomain}>"), "from" },
        { new StringContent(toEmail), "to" },
        { new StringContent("Welcome to Sesi Mathebe Remote Extra Classes!"), "subject" },
        { new StringContent(emailText), "text" }
    };

            var response = await client.PostAsync($"{baseUrl}/{sandboxDomain}/messages", form);
            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Mailgun Response: {response.StatusCode} - {content}");
        }
    }

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
}