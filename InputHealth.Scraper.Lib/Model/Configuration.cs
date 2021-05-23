namespace InputHealth.Scraper.Lib
{
    public class Configuration
    {
        public Location[] locations { get; set; }
        public Service[] services { get; set; }
        public Setting[] settings { get; set; }
        public ServiceOnTimeMapping[] services_mapped_with_on_time { get; set; }
    }

    public class Location
    {
        public int? id { get; set; }
        public string name { get; set; }
        public bool @public { get; set; }
        public string full_address { get; set; }
        public string latitude { get; set; }
        public string longitude { get; set; }
    }

    public class Service
    {
        public int id { get; set; }
        public string name { get; set; }
        public int slot_length { get; set; } // duration of service in mintues
        public bool allow_new_respondent { get; set; } // first apt
        public bool allow_existing_respondent { get; set; } // second apt (follow up)
    }

    public class Setting
    {
        public int resource_id { get; set; }
        public string resource_type { get; set; }
        public int slots { get; set; }
        public int slot_length { get; set; }
        public bool allow_new_respondent { get; set; }
        public bool location_visible_to_public { get; set; }
    }

    public class ServiceOnTimeMapping
    {
        public int service_id { get; set; }
        public int location_id { get; set; }
        public int provider_user_id { get; set; }
    }
}
