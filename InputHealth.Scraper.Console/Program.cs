using InputHealth.Scraper.Lib;
using System;
using System.Linq;

namespace InputHealth.Scraper.CLI
{
    class Program
    {
        static async System.Threading.Tasks.Task Main(string[] args)
        {
            InputHealthAPIClient.EMULATE_DATA = false;

            var startDate = new DateTime(2021, 6, 1);
            var endDate = startDate.AddMonths(4);
            var availability = await InputHealthAPIClient.GetAvailabilityAsync(startDate, endDate);

            var availableIntervals = (from x in availability
                                      let Availability = x.DailyAvailable.Where(y => y.Value > 0).ToArray()
                                      select new
                                      {
                                          Location = x,
                                          LocationName = $"{x.Name}{(x.IsPublic ? "" : " (PHONE ONLY)")}",
                                          Availability = Availability
                                      }).ToArray();

            foreach (var location in availableIntervals)
            {
                Console.WriteLine($"{location.LocationName}: ");
                foreach (var interval in location.Availability)
                {
                    Console.WriteLine($"\t{interval.Key.ToString("M")} - {interval.Value} available");
                }
            }
        }
    }
}
