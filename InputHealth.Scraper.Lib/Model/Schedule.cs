using System;
using System.Collections.Generic;
using System.Linq;

namespace InputHealth.Scraper.Lib.Model
{
    public class Schedule
    {
        public DateTimeOffset from { get; set; }
        public DateTimeOffset to { get; set; }

        public OnTime[] on_times { get; set; }
        public ProviderUserOffTime[] provider_user_off_times { get; set; }
        public Appointment[] appointments { get; set; }
        public string appointments_csv { get; set; }

        public Appointment[] GetAppointments()
        {
            return appointments
                ?? appointments_csv
                    .Split('\n')
                    .Skip(1)
                    .Select(a => a.Split(','))
                    .Where(a => a.Length == 4)
                    .Select(a => new Appointment
                    {
                        id = Convert.ToInt32(a[0]),
                        provider_user_id = Convert.ToInt32(a[1]),
                        start_at = DateTimeOffset.Parse(a[2]),
                        until_at = DateTimeOffset.Parse(a[3])
                    }).ToArray();
        }
    }

    public class OnTime
    {
        public int id { get; set; }
        public int resource_id { get; set; }
        public string resource_type { get; set; }

        public DateTimeOffset from { get; set; }
        public DateTimeOffset until { get; set; }
        public int duration { get; set; }

        public FlexibleHour flexible_hour { get; set; }
    }

    public class FlexibleHour
    {
        public int provider_user_id { get; set; }
        public int location_id { get; set; }
        public DateTimeOffset? start_time { get; set; }
        public DateTimeOffset? end_time { get; set; }
        public TimeSpan? duration => end_time - start_time;
        public int slots { get; set; }
        public int[] service_ids { get; set; }
        public Dictionary<int, int> intervals_by_service_ids { get; set; }
        public int on_time_id { get; set; }
    }

    public class ProviderUserOffTime
    {
        public int resource_id { get; set; }
        public string resource_type { get; set; }

        public DateTimeOffset from { get; set; }
        public DateTimeOffset until { get; set; }
        public TimeSpan duration => until - from;
    }

    public class Appointment
    {
        public int id { get; set; }
        public int provider_user_id { get; set; }

        public DateTimeOffset start_at { get; set; }
        public DateTimeOffset until_at { get; set; }
        public TimeSpan duration => until_at - start_at;
    }
}
