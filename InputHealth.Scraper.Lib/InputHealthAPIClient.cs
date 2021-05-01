using InputHealth.Scraper.Lib.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace InputHealth.Scraper.Lib
{
    public class InputHealthAPIClient
    {
        public static bool EMULATE_DATA = false;

        public static async Task<Configuration> GetConfiguration()
        {
            const string emulationPath = "EmulatedData/Configuration.json";

            var rawJson = "";
            if (EMULATE_DATA)
            {
                rawJson = await File.ReadAllTextAsync(emulationPath);
            }
            else
            {
                var httpClient = new HttpClient();
                rawJson = await httpClient.GetStringAsync("https://peelregion.region-of-peel.inputhealth.com/public/appointments/configuration");
#if DEBUG
                await File.WriteAllTextAsync(emulationPath, rawJson);
#endif
            }

            return JsonSerializer.Deserialize<Configuration>(rawJson);
        }

        public static async Task<Schedule> GetSchedule() => await LoadSchedule(DateTime.UtcNow, DateTime.UtcNow.AddMonths(1));
        public static async Task<Schedule> LoadSchedule(DateTime from, DateTime to)
        {
            const string emulationPath = "EmulatedData/Schedule.json";

            var rawJson = "";
            if (EMULATE_DATA)
            {
                rawJson = await File.ReadAllTextAsync(emulationPath);
            }
            else
            {
                var httpClient = new HttpClient();
                rawJson = await httpClient.GetStringAsync($"https://peelregion.region-of-peel.inputhealth.com/public/appointments/schedules?from={from.ToString("yyyy-MM-dd")}&to={to.ToString("yyyy-MM-dd")}&practitioner_id=");
#if DEBUG
                await File.WriteAllTextAsync(emulationPath, rawJson);
#endif
            }

            return JsonSerializer.Deserialize<Schedule>(rawJson);
        }

        public static async Task<LocationAvailability[]> GetAvailabilityAsync()
        {
            var cutOffTime = DateTimeOffset.Now.AddMinutes(-30);

            var config = await GetConfiguration();
            var schedule = await GetSchedule();

            var locationAvailabilities = new Dictionary<int, LocationAvailability>();

            // Load in availability intervals
            foreach (var onTime in schedule.on_times.OrderBy(x => x.from).ThenBy(x => x.until))
            {
                var locationId = onTime.flexible_hour.location_id;

                if (!locationAvailabilities.TryGetValue(locationId, out var location))
                {
                    location = new LocationAvailability
                    {
                        location = config.locations.Where(x => x.id == locationId).FirstOrDefault()
                    };
                    locationAvailabilities[locationId] = location;
                }

                location.on_times.Add(onTime);
                location.ProviderUserIds.Add(onTime.resource_id);

                var anyPublicServices = onTime.flexible_hour.service_ids.Any(x => config.services.FirstOrDefault(y => y.id == x)?.allow_new_respondent ?? false);
                if (!anyPublicServices)
                {
                    continue;
                }

                var providerOffTime = schedule.provider_user_off_times.OrderBy(x => x.from).Where(x => x.resource_type == onTime.resource_type && x.resource_id == onTime.resource_id);

                var interval = onTime.from;
                while (interval.AddMinutes(15) < onTime.until)
                {
                    if (interval >= cutOffTime)
                    {
                        var intervalOffTime = providerOffTime.Where(x => interval >= x.from && interval < x.until);
                        if (!intervalOffTime.Any())
                        {
                            if (!location.IntervalCapacity.ContainsKey(interval))
                            {
                                location.IntervalCapacity[interval] = 0;
                            }
                            location.IntervalCapacity[interval] += onTime.flexible_hour.slots;
                        }
                        else
                        {
                            location.provider_user_off_times.AddRange(intervalOffTime);
                        }
                    }
                    interval = interval.AddMinutes(15); // Assume 15 minutes for all apts, seems to be the case so far
                }
            }

            var brokenAppointmentRepairMapping = new Dictionary<int, int>()
            {
                { 1301, 29 }, // international centre
                { 1761, 28 }, // brampton soccer
                { 1302, 30 }, // paramount
                { 152, 9 }, // caledon
            };

            // Load in booked intervals
            foreach (var apt in schedule.appointments.OrderBy(x => x.start_at))
            {
                var location = locationAvailabilities.Values.Where(x => x.ProviderUserIds.Contains(apt.provider_user_id)).FirstOrDefault()
                    ?? locationAvailabilities.Values.FirstOrDefault(x => x.Id == config.services_mapped_with_on_time.Where(x => x.provider_user_id == apt.provider_user_id).FirstOrDefault()?.location_id) // check if the mapping to location exists in config
                    ?? locationAvailabilities.Values.Where(x => x.on_times.Any(y => y.id == apt.provider_user_id)).FirstOrDefault() //in some cases they book against the on time ID somehow, bug?
                    ?? locationAvailabilities.Values.FirstOrDefault(x => x.Id == brokenAppointmentRepairMapping.FirstOrDefault(k => k.Key == apt.provider_user_id).Value); //provider users that just dont show up in the schedule data for some reason, manual mapping through name guesses
                if (location == null)
                {
                    //HMMMM - other cases unknown thus far.
                    continue;
                }

                location.booked_appointments.Add(apt);

                var interval = apt.start_at;

                if (interval < cutOffTime)
                {
                    // In the past, lets just move on.
                    continue;
                }

                if (!location.IntervalBooked.ContainsKey(interval) && !location.IntervalBooked.TryAdd(interval, 0))
                {
                    //HMMMMM
                    throw new Exception("We shouldn't get here tbh");
                }

                location.IntervalBooked[interval]++;
            }

            // Calculate availability
            foreach (var location in locationAvailabilities.Values)
            {
                foreach (var intervalCapacity in location.IntervalCapacity)
                {
                    var date = intervalCapacity.Key;

                    var bookedOnInterval = location.IntervalBooked.Where(k => k.Key >= date && k.Key < date.AddMinutes(15));

                    if (bookedOnInterval.Count() > 1)
                    {
                        //HMMMMM - this means the apt is overlapping and we may overreport availability through summing. we should probably have a way to pick or subtract from both or something
                    }

                    location.IntervalAvailable[date] = intervalCapacity.Value - bookedOnInterval.Sum(x => x.Value);
                }
            }

            return locationAvailabilities.Values.ToArray();
        }
    }
}
