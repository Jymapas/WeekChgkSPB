var httpClient = new HttpClient();
var response = await httpClient.GetAsync("https://chgk-spb.livejournal.com/data/rss");
var r = await response.Content.ReadAsStringAsync();
Console.WriteLine(r);
Console.ReadKey();