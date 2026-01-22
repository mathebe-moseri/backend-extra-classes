using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace extra_classes.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class extraClass : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public extraClass(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet("get_learnerInfo")]
        public JsonResult get_learnerInfo()
        {
            string query = "SELECT * FROM ClassRegistrations";
            DataTable table = new DataTable();
            string sqlDataSource = _configuration.GetConnectionString("extra-classes");

            using (SqlConnection myCon = new SqlConnection(sqlDataSource))
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

        // ✅ ADD LEARNER + SEND EMAIL
        [HttpPost("add_learnerInfo")]
        public async Task<IActionResult> addLearnerInfo([FromBody] ClassRegistration learner)
        {
            if (learner == null)
                return BadRequest(new { message = "Learner data is null" });

            string query = @"
                INSERT INTO ClassRegistrations
                (LearnerFirstName, LearnerSurname, Grade, Email, SchoolName, ParentFullName, ParentCell)
                VALUES
                (@LearnerFirstName, @LearnerSurname, @Grade, @Email, @SchoolName, @ParentFullName, @ParentCell)";

            string sqlDataSource = _configuration.GetConnectionString("extra-classes");

            using (SqlConnection conn = new SqlConnection(sqlDataSource))
            {
                await conn.OpenAsync();
                using (SqlCommand cmd = new SqlCommand(query, conn))
                {
                    cmd.Parameters.AddWithValue("@LearnerFirstName", learner.LearnerFirstName);
                    cmd.Parameters.AddWithValue("@LearnerSurname", learner.LearnerSurname);
                    cmd.Parameters.AddWithValue("@Grade", learner.Grade);
                    cmd.Parameters.AddWithValue("@Email", learner.Email);
                    cmd.Parameters.AddWithValue("@SchoolName", learner.SchoolName);
                    cmd.Parameters.AddWithValue("@ParentFullName", learner.ParentFullName);
                    cmd.Parameters.AddWithValue("@ParentCell", learner.ParentCell);

                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // ✉️ SEND WELCOME EMAIL
            await SendWelcomeEmail(learner);

            return Ok(new { message = "Registration successful. Email sent." });
        }

        // 📩 EMAIL METHOD
        private async Task SendWelcomeEmail(ClassRegistration learner)
        {
            var apiKey = _configuration["SendGridApiKey"];
            var joinUrl = _configuration["Frontend:JoinSessionUrl"];

            var client = new SendGridClient(apiKey);

            var from = new EmailAddress("connymoseri0303@gmail.com", "Sesi Mathebe Extra Classes");
            var to = new EmailAddress(learner.Email, learner.LearnerFirstName);

            var subject = "Welcome to Sesi Mathebe Extra Classes 🎓";

            var plainText = $@"
Hi {learner.LearnerFirstName},

Welcome to Sesi Mathebe Extra Classes!

Please click the link below to join our next session:
{joinUrl}

We look forward to learning with you.
";

            var html = $@"
<h2>Welcome to Sesi Mathebe Extra Classes 🎓</h2>
<p>Hi <strong>{learner.LearnerFirstName}</strong>,</p>
<p>Thank you for joining <strong>Sesi Mathebe Extra Classes</strong>.</p>
<p>
  👉 <a href='{joinUrl}' target='_blank'>
     Click here to join our next session
  </a>
</p>
<p>We look forward to learning with you!</p>
<br/>
<p><strong>Sesi Mathebe Team</strong></p>";

            var msg = MailHelper.CreateSingleEmail(from, to, subject, plainText, html);

            await client.SendEmailAsync(msg);
        }
    }

    // MODEL
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
