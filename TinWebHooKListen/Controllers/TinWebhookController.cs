using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using Amazon.SQS;
using Amazon.SQS.Model;
using Newtonsoft.Json;
using System.Data.SqlClient;
using System.Configuration;

namespace TinWebHooKListen.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TinWebhookController : ControllerBase
    {
        //WeatherForecastController
        private static string responseQueueUrl = "";
        private static AmazonSQSClient sqsClient;
        private static string ConnectionString;

        public TinWebhookController(ILogger<TinWebhookController> logger)
        {
            string accessKey = ConfigurationManager.AppSettings["AccessKey"];
            string secretKey = ConfigurationManager.AppSettings["SecretKey"];

            sqsClient = new AmazonSQSClient(accessKey, secretKey, Amazon.RegionEndpoint.APSoutheast2);
            
            ConnectionString = ConfigurationManager.AppSettings["ConnectionString"];
            responseQueueUrl = ConfigurationManager.AppSettings["ResponseQueueUrl"];
        }

        [HttpPost]
        public string Post([FromBody] StatusResponse StatusResponse)
        {
            int StatusCode = (int)HttpStatusCode.OK;
            string messageBody = "Message Received";

            try
            {
                using (SqlConnection conn = new SqlConnection())
                {
                    conn.ConnectionString = ConnectionString;
                    conn.Open();

                    SqlCommand command = new SqlCommand("SELECT * FROM [dbo].[transactions] where [complaincelyId] = @compId", conn);

                    command.Parameters.Add(new SqlParameter("compId", StatusResponse.id));

                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        //check if there are more than one records
                        while (reader.Read())
                        {
                            Console.WriteLine(String.Format("{0} \t | {1} \t | {2} \t | {3}", reader[0], reader[1], reader[2], reader[3]));
                            string complaincelyId = String.Format("{0}", reader["complaincelyId"]);
                            string tax1099Id = String.Format("{0}", reader["tax1099Id"]);
                            StatusResponse.tax1099Id = tax1099Id;
                             
                            using (SqlConnection updateConnection = new SqlConnection())
                            {
                                updateConnection.ConnectionString = ConnectionString;
                                updateConnection.Open();

                                SqlCommand updateCommand = new SqlCommand("UPDATE [dbo].[transactions] SET status = @status, irs_code = @irs_code WHERE complaincelyId = @compId ; ", updateConnection);
                                updateCommand.Parameters.Add(new SqlParameter("status", StatusResponse.status));
                                updateCommand.Parameters.Add(new SqlParameter("irs_code", StatusResponse.irs_code));
                                updateCommand.Parameters.Add(new SqlParameter("compId", complaincelyId));
                                updateCommand.ExecuteNonQuery();
                                updateConnection.Close();
                            }
                        }
                    }
                    conn.Close();
                    Console.WriteLine("Database update done\n");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error while updating transactional logs {0}", e);
                StatusCode = 500;
                messageBody = "Couldn't process request, Failed while Error while updating DB";
            }

            try
            {
                string msg = JsonConvert.SerializeObject(StatusResponse);

                SendMessageResponse sendingMessageResponse = Task.Run(async () => await sqsClient.SendMessageAsync(responseQueueUrl, msg)).Result;

                Console.WriteLine(" Message" + StatusResponse.id + " sent to queue. Http response code" + sendingMessageResponse.HttpStatusCode.ToString());

            }
            catch (Exception e)
            {
                Console.WriteLine("Error while sending message to queue {0}", e);
                StatusCode = 500;
                messageBody = "Couldn't process request, Failed while Error while sending message to response queue ";
            }

            return @"{ ""StatusCode"":" + StatusCode + @" , ""Body"" :" + messageBody + @" })";
        }
    }
}
