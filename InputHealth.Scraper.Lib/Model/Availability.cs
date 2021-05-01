﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace InputHealth.Scraper.Lib.Model
{
    public class LocationAvailability
    {
        public int Id => location.id.HasValue ? location.id.Value : -1;
        public string Name => location.name;
        public bool IsPublic => location.@public;

        public Dictionary<DateTimeOffset, int> IntervalCapacity { get; set; } = new Dictionary<DateTimeOffset, int>();
        public Dictionary<DateTimeOffset, int> IntervalBooked { get; set; } = new Dictionary<DateTimeOffset, int>();
        public Dictionary<DateTimeOffset, int> IntervalAvailable { get; set; } = new Dictionary<DateTimeOffset, int>();

        public int TotalCapacity => IntervalCapacity.Sum(x => x.Value);
        public int TotalBooked => IntervalBooked.Sum(x => x.Value);
        public int TotalAvailable => IntervalAvailable.Sum(x => x.Value);

        public HashSet<int> ProviderUserIds { get; set; } = new HashSet<int>();

        // Raw data
        public Location location { get; set; }
        public List<OnTime> on_times { get; set; } = new List<OnTime>();
        public List<ProviderUserOffTime> provider_user_off_times { get; set; } = new List<ProviderUserOffTime>();
        public List<Appointment> booked_appointments { get; set; } = new List<Appointment>();
    }
}
