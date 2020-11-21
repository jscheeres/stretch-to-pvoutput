using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace stretchtopvoutput
{
    public class StretchToPvOutput
    {
        private readonly HttpClient _stretchClient;
        private readonly HttpClient _pvOutputClient;

        public StretchToPvOutput()
        {
            _stretchClient = new HttpClient();
            _stretchClient.BaseAddress = new Uri(Environment.GetEnvironmentVariable("StretchUri"));
            _pvOutputClient = new HttpClient();
            _pvOutputClient.BaseAddress = new Uri("https://pvoutput.org");
        }

        [FunctionName("StretchToPvOutput")]
        public async Task Run([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, ILogger log)
        {
            try {
                log.LogInformation($"SolarReading started: {DateTime.Now}");

                var strechUsername = Environment.GetEnvironmentVariable("StretchUsername");
                var strechPassword = Environment.GetEnvironmentVariable("StretchPassword");

                var byteArray = Encoding.ASCII.GetBytes($"{strechUsername}:{strechPassword}");
                _stretchClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
                var stretchRawResponse = await _stretchClient.GetAsync("/core/appliances");

                if(!stretchRawResponse.IsSuccessStatusCode)
                {
                    log.LogError("Error calling Stretch device");
                }

                var stretchXmlResponse = await stretchRawResponse.Content.ReadAsStringAsync();
                
                log.LogInformation($"Raw stretch response: {stretchXmlResponse}");

                var xdoc = XDocument.Parse(stretchXmlResponse);
                var pointLogElements = xdoc.Descendants().Where(n => n.Name == "point_log");
                var electricityProducedElement = pointLogElements
                                                    .Descendants()
                                                    .FirstOrDefault(pl => pl.Name == "type" && pl.Value == "electricity_produced")
                                                    .Parent
                                                    .Descendants()
                                                    .FirstOrDefault(pl => pl.Name == "measurement");
                var electricityProduced = electricityProducedElement.Value.Substring(0, electricityProducedElement.Value.IndexOf("."));
                var timestamp = DateTime.Parse(electricityProducedElement.Attributes().FirstOrDefault(e => e.Name == "log_date").Value, new System.Globalization.CultureInfo("nl-NL"));
                
                log.LogInformation($"{electricityProduced} W electricity produced on {timestamp}");
                log.LogInformation($"SolarReading finished: {DateTime.Now}");
                log.LogInformation($"Upload reading to PVOutput: {DateTime.Now}");
                
                _pvOutputClient.DefaultRequestHeaders.Add("X-Pvoutput-Apikey", "6c90916a283c68bfba55ac8deed9efeed9080499");
                _pvOutputClient.DefaultRequestHeaders.Add("X-Pvoutput-SystemId", "23608");
                
                var d = timestamp.ToString("yyyyMMdd");
                log.LogInformation($"d: {d}");
                var t = $"{timestamp.Hour.ToString().PadLeft(2, '0')}:{timestamp.Minute.ToString().PadLeft(2, '0')}";
                log.LogInformation($"t: {t}");
                var v2 = electricityProduced;
                log.LogInformation($"v2: {v2}");

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("d", d),
                    new KeyValuePair<string, string>("t", t),
                    new KeyValuePair<string, string>("v2", v2)
                });

                content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                
                var pvOutputRawResponse = await _pvOutputClient.PostAsync("service/r2/addstatus.jsp", content);
                
                if(!pvOutputRawResponse.IsSuccessStatusCode)
                {
                    log.LogError("Error calling PVOutput");
                }
            }
            catch(Exception ex){
                log.LogError("Error!", ex);
            }
        }
    }
}
