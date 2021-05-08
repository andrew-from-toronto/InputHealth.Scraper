using Azure.Storage.Blobs.Specialized;
using InputHealth.Scraper.Lib;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace InputHealth.Scraper.Job
{
    public class OutputAvailability
    {
        public int? LocationId { get; set; }
        public string LocationName { get; set; }
        public bool LocationPublic { get; set; }
        public KeyValuePair<DateTimeOffset, int>[] Availability { get; set; }
        public KeyValuePair<DateTimeOffset, int>[] Booked { get; set; }
    }

    public static class InputHealthScraperFunction
    {
        private readonly static Azure.Storage.Blobs.Models.BlockBlobOpenWriteOptions _blobOptions = new Azure.Storage.Blobs.Models.BlockBlobOpenWriteOptions
        {
            HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
            {
                ContentType = "application/json"
            }
        };

        [FunctionName("VaccinePeelReloadConfiguration")]
        public static async Task VaccinePeelReloadConfiguration(
            ILogger log,
            [TimerTrigger("0 */5 * * * *")] TimerInfo myTimer,
            [Blob("generated/configuration.json", FileAccess.ReadWrite, Connection = "OutputStorage")] BlockBlobClient configurationJson)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            var configuration = await InputHealthAPIClient.GetConfiguration(TimeSpan.FromMinutes(15));

            using var writeStream = await configurationJson.OpenWriteAsync(true, _blobOptions);
            await JsonSerializer.SerializeAsync(writeStream, configuration);
        }

        [FunctionName("VaccinePeelScrapeTimer")]
        public static async Task VaccinePeelScrapeTimer(
            ILogger log,
            [TimerTrigger("30 */5 * * * *")] TimerInfo myTimer,
            [Blob("generated/configuration.json", FileAccess.Read, Connection = "OutputStorage")] BlockBlobClient configurationJson,
            [Blob("generated/availability.json", FileAccess.ReadWrite, Connection = "OutputStorage")] BlockBlobClient availabilityJson)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            Configuration configuration;
            using (var readStream = await configurationJson.OpenReadAsync())
            {
                configuration = await JsonSerializer.DeserializeAsync<Configuration>(readStream);
            }

            var availability = await InputHealthAPIClient.GetAvailabilityAsync(configuration, TimeSpan.FromMinutes(15));

            var availableIntervals = (from x in availability
                                      orderby x.Id ascending
                                      let Availability = x.DailyAvailable.Where(y => y.Value > 0).ToArray()
                                      select new OutputAvailability
                                      {
                                          LocationId = x.Id,
                                          LocationName = $"{x.Name}",
                                          LocationPublic = x.IsPublic,
                                          Availability = Availability,
                                          Booked = x.DailyBooked.ToArray()
                                      }).ToArray();

            /*OutputAvailability[] prevAvailableIntervals;
            using (var openStream = await availabilityJson.OpenReadAsync(new Azure.Storage.Blobs.Models.BlobOpenReadOptions(false)))
            {
                prevAvailableIntervals = await JsonSerializer.DeserializeAsync<OutputAvailability[]>(openStream);
            }*/

            using (var writeStream = await availabilityJson.OpenWriteAsync(true, _blobOptions))
            {
                await JsonSerializer.SerializeAsync(writeStream, availableIntervals);
            }

            // notifications
            /*
            foreach (var location in availableIntervals)
            {
                var prevLocation = prevAvailableIntervals.FirstOrDefault(x => x.LocationId.HasValue && x.LocationId.Value == location.LocationId);
                if (prevLocation == null)
                {
                    continue; // no data from previous run
                }

                var subjectAvailability = new List<string>();

                foreach (var interval in location.Availability)
                {
                    var prevInterval = prevLocation.Availability.Where(x => x.Key == interval.Key);
                    if (!prevInterval.Any())
                    {
                        continue; // no data from previous run
                    }

                    var newAvailability = interval.Value;
                    var prevAvailability = interval.Value;

                    //We have availability now 
                    if (prevAvailability <= 0 && newAvailability > 0)
                    {
                        subjectAvailability.Add($"{newAvailability} slots {interval.Key:M (ddd)}");
                    }
                }

                if (subjectAvailability.Count > 0)
                {
                    var sender = new MailgunSender(
                            "alert.vaccine-peel.ca", // Mailgun Domain
                            "" // Mailgun API Key
                        );
                    Email.DefaultSender = sender;
                    var email = Email
                        .From("availability@alert.vaccine-peel.ca")
                        .To("vaccinepeelca@googlegroups.com")
                        .Subject($"{location.LocationName} has {string.Join(", ", subjectAvailability)}")
                        .Body("To book head over to https://peelregion.inputhealth.com/ebooking. Good luck, appointments go fast. Visit https://www.vaccine-peel.ca for a snapshot of availability.");

                    var response = await email.SendAsync();
                }
            }*/
        }

        [FunctionName("VaccinePeelFollowUpsScrapeTimer")]
        [Disable]
        public static async Task VaccinePeelFollowUpsScrapeTimer(ILogger log,
            [TimerTrigger("0 0 20-6 * * *")] TimerInfo myTimer,
            [Blob("generated/configuration.json", FileAccess.Read, Connection = "OutputStorage")] BlockBlobClient configurationJson,
            [Blob("generated/availability-followups.json", FileAccess.ReadWrite, Connection = "OutputStorage")] BlockBlobClient availabilityJson)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.UtcNow}");

            var followUpStartDate = DateTime.UtcNow.AddDays((7 * 6) + 1); // Start 6 weeks + 1 day ahead 

            Configuration configuration;
            using (var readStream = await configurationJson.OpenReadAsync())
            {
                configuration = await JsonSerializer.DeserializeAsync<Configuration>(readStream);
            }
            var availability = await InputHealthAPIClient.GetAvailabilityAsync(followUpStartDate, followUpStartDate.AddDays(7 * 6), configuration, TimeSpan.FromMinutes(5));

            var availableIntervals = (from x in availability
                                      orderby x.Id ascending
                                      let Availability = x.DailyAvailable.Where(y => y.Value > 0).ToArray()
                                      select new OutputAvailability
                                      {
                                          LocationId = x.Id,
                                          LocationName = $"{x.Name}",
                                          LocationPublic = x.IsPublic,
                                          Availability = Availability,
                                          Booked = x.DailyBooked.ToArray()
                                      }).ToArray();

            using (var writeStream = await availabilityJson.OpenWriteAsync(true))
            {
                await JsonSerializer.SerializeAsync(writeStream, availableIntervals);
            }

            await availabilityJson.SetHttpHeadersAsync(new Azure.Storage.Blobs.Models.BlobHttpHeaders
            {
                ContentType = "application/json"
            });
        }
    }
}
