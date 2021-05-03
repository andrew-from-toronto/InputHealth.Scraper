# https://www.vaccine-peel.ca/ / InputHealth Scraper
This is a library designed to scrape public appointment availability from the InputHealth booking system Peel Public Health is using over at https://peelregion.inputhealth.com/ebooking

This scraping library implements a simplified availability model based on the way InputHealth is being used for the COVID-19 vaccine booking process. This library explicitly does not support stacked intervals, appointments shorter than 15 minutes, and probably many other cases. The numbers are reliable at the moment, however please feel free to submit an issue or, even better, a PR for any bugs you may find.

I welcome all to use the data generated from this code, available [here](https://vaccinepeelcapublic.blob.core.windows.net/generated/availability.json)