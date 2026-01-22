using Microsoft.AspNetCore.Mvc;
using System.Data;
using System.Data.SqlClient;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Data.SqlClient;

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

        [HttpGet("get_learnerInfo")]
        public JsonResult get_learnerInfo()
        {
            string query = "SELECT * FROM ClassRegistrations";

            DataTable table = new DataTable();

            string SqlDataSource = _configuration.GetConnectionString("extra-classes");

            SqlDataReader myReader;
            using (SqlConnection myCon = new SqlConnection(SqlDataSource))
            {
                myCon.Open();
                using (SqlCommand myCommand = new SqlCommand(query, myCon))
                {
                    myReader = myCommand.ExecuteReader();
                    table.Load(myReader);
                }
            }
            return new JsonResult(table);
        }

        [HttpPost("add_learnerInfo")]
        public JsonResult addLearnerInfo([FromBody] ClassRegistration learner)
        {
            if (learner == null)
            {
                return new JsonResult(new { message = "Learner data is null" });
            }

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

            return new JsonResult(new { message = "Learner added successfully" });
        }
    }

    // Model for ClassRegistrations
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
