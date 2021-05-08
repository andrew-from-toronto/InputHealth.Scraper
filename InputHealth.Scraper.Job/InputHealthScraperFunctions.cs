using Azure.Storage.Blobs.Specialized;
using FluentEmail.Core;
using FluentEmail.Mailgun;
using InputHealth.Scraper.Lib;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
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

    public class InputHealthScraperFunctions
    {
#if DEBUG
        const bool DEBUG_MODE = true;
#else
        const bool DEBUG_MODE = false;
#endif

        private readonly static Azure.Storage.Blobs.Models.BlockBlobOpenWriteOptions _blobOptions = new Azure.Storage.Blobs.Models.BlockBlobOpenWriteOptions
        {
            HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
            {
                ContentType = "application/json"
            }
        };

        private readonly IConfiguration _configuration;
        private string MailgunAPIKey => _configuration.GetValue<string>("Values:MailgunAPIKey");

        public InputHealthScraperFunctions(IConfiguration configuration)
            => _configuration = configuration;

#if DEBUG
        [Disable]
#endif
        [FunctionName("VaccinePeelReloadConfiguration")]
        public async Task VaccinePeelReloadConfiguration(
            ILogger log,
            [TimerTrigger("0 14,29,44,59 * * * *")] TimerInfo myTimer,
            [Blob("generated/configuration.json", FileAccess.ReadWrite, Connection = "OutputStorage")] BlockBlobClient configurationJson)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            var configuration = await InputHealthAPIClient.GetConfiguration(TimeSpan.FromMinutes(15));

            using var writeStream = await configurationJson.OpenWriteAsync(true, _blobOptions);
            await JsonSerializer.SerializeAsync(writeStream, configuration);
        }

        [FunctionName("VaccinePeelScrapeTimer")]
        public async Task VaccinePeelScrapeTimer(
            ILogger log,
            [TimerTrigger("0 */5 * * * *", RunOnStartup = DEBUG_MODE)] TimerInfo myTimer,
            [Blob("generated/configuration.json", FileAccess.Read, Connection = "OutputStorage")] BlockBlobClient configurationJson,
            [Blob("generated/availability.json", FileAccess.ReadWrite, Connection = "OutputStorage")] BlockBlobClient availabilityJson)
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            Configuration configuration = null;
            if (await configurationJson.ExistsAsync())
            {
                using var readStream = await configurationJson.OpenReadAsync();
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

            OutputAvailability[] prevAvailableIntervals = Array.Empty<OutputAvailability>();
            if (await availabilityJson.ExistsAsync())
            {
                using var openStream = await availabilityJson.OpenReadAsync(new Azure.Storage.Blobs.Models.BlobOpenReadOptions(false));
                prevAvailableIntervals = await JsonSerializer.DeserializeAsync<OutputAvailability[]>(openStream);
            }

            using (var writeStream = await availabilityJson.OpenWriteAsync(true, _blobOptions))
            {
                await JsonSerializer.SerializeAsync(writeStream, availableIntervals);
            }

            // notifications
            var emailLocationsAvailability = new Dictionary<string, string>(); //location -> email text block
            foreach (var location in availableIntervals)
            {
                // Do not notify non-public locations
                if (!location.LocationPublic) { continue; }

                var prevLocation = prevAvailableIntervals.FirstOrDefault(x => x.LocationId.HasValue && x.LocationId.Value == location.LocationId);
                if (prevLocation == null)
                {
                    continue; // no data from previous run
                }

                var locationAvailability = new List<string>();

                foreach (var interval in location.Availability)
                {
                    var prevInterval = prevLocation.Availability.Where(x => x.Key == interval.Key);
                    if (!prevInterval.Any())
                    {
                        continue; // no data from previous run
                    }

                    var newAvailability = interval.Value;
                    var prevAvailability = interval.Value;

                    // We have availability now 
                    if (prevAvailability <= 0 && newAvailability >= 3)
                    {
                        var deltaAvailability = newAvailability - prevAvailability;
                        locationAvailability.Add($" - {interval.Key:MMM dd} - {newAvailability} appointments (+{deltaAvailability})");
                    }
                }

                emailLocationsAvailability[location.LocationName] = string.Join("\n", locationAvailability);
            }

            if (emailLocationsAvailability.Count > 0)
            {
                var emailBody = string.Join("\n\n", emailLocationsAvailability.Select(kvp => $"{kvp.Key}\n{kvp.Value}"));

                log.LogInformation("Sending availability email with {body}", emailBody);

                var sender = new MailgunSender(
                        "alert.vaccine-peel.ca",
                        MailgunAPIKey
                    );
                Email.DefaultSender = sender;
                var email = Email
                    .From("availability@alert.vaccine-peel.ca")
                    .ReplyTo("noreply@alert.vaccine-peel.ca")
                    .To("vaccinepeelca@googlegroups.com")
                    .Subject($"Alert: New Appointments Available on Peel's Booking System!")
                    .Body($"To book any of these appointments head over to https://peelregion.inputhealth.com/ebooking. \n\n{emailBody} \n\nVisit https://www.vaccine-peel.ca for a snapshot of availability.");

                var response = await email.SendAsync();
            }
        }

        [FunctionName("VaccinePeelFollowUpsScrapeTimer")]
        [Disable]
        public async Task VaccinePeelFollowUpsScrapeTimer(ILogger log,
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
