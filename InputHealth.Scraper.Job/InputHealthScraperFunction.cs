using InputHealth.Scraper.Lib;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace InputHealth.Scraper.Job
{
    public static class InputHealthScraperFunction
    {
        [FunctionName("VaccinePeelScrapeTimer")]
        public static async Task RunAsync([TimerTrigger("0 */5 * * * *")] TimerInfo myTimer, ILogger log,
            [Blob("generated/availability.json", FileAccess.Write, Connection = "OutputStorage")] Stream availabilityJson)
        {
            InputHealthAPIClient.EMULATE_DATA = false;

            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            var availability = await InputHealthAPIClient.GetAvailabilityAsync();

            var availableIntervals = (from x in availability
                                      where x.IsPublic
                                      let Availability = x.IntervalAvailable.Where(y => y.Value > 0).ToArray()
                                      where Availability.Any()
                                      select new
                                      {
                                          LocationName = x.Name,
                                          Availability = Availability
                                      }).ToArray();

            await JsonSerializer.SerializeAsync(availabilityJson, availableIntervals);
        }
    }
}
