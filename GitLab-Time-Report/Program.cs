using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
class Program
{
    static async Task Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddUserSecrets<Program>()
            .Build();

        // GitLab API URL
        string apiUrl = config["ApiUrl"];
        // Personal access token with API access
        string accessToken = config["ApiKey"];

        // GitLab user ID
        int userId = int.Parse(args[0]);

        // Initialize HttpClient
        using (HttpClient client = new HttpClient())
        {
            // Set the base URL of the API
            client.BaseAddress = new Uri(apiUrl);

            // Set the authorization header with the access token
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

            // Get the projects for the user asynchronously
            Task<List<Project>> projectsTask = GetProjectsForUser(client, userId);

            // Wait for the task to complete
            List<Project> projects = await projectsTask;

            // Create a list of tasks to fetch time spent for each project
            List<Task<Tuple<int, Dictionary<int, Tuple<string, int>>>>> timeSpentTasks = new List<Task<Tuple<int, Dictionary<int, Tuple<string, int>>>>>();
            foreach (Project project in projects)
            {
                Task<Tuple<int, Dictionary<int, Tuple<string, int>>>> timeSpentTask = GetTimeSpentForProject(client, project.Id, userId);
                timeSpentTasks.Add(timeSpentTask);
            }

            // Wait for all time spent tasks to complete
            //Task<int>[] timeSpentTaskArray = timeSpentTasks.ToArray();
            await Task.WhenAll(timeSpentTasks);

            // Retrieve the results of time spent tasks
            int totalTimeSpent = 0;
            for (int i = 0; i < projects.Count; i++)
            {
                Project project = projects[i];
                //int timeSpentSeconds = timeSpentTaskArray[i].Result;
                int timeSpentSeconds = timeSpentTasks[i].Result.Item1;
                if (timeSpentSeconds == 0) continue;

                totalTimeSpent += timeSpentSeconds;
                string formattedTimeSpent = FormatTimeSpent(timeSpentSeconds);
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine($"Project: {project.Name}, Time Spent: {formattedTimeSpent}");
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                var tasks = timeSpentTasks[i].Result.Item2;
                foreach (var item in tasks)
                {
                    if (item.Value.Item2 > 0) Console.WriteLine($"\t Time Spent: {FormatTimeSpent(item.Value.Item2)} | Issue: {item.Key} - {item.Value.Item1}");
                }
            }
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine();
            Console.WriteLine($"Total time spent: {FormatTimeSpent(totalTimeSpent)}");
        }

        // Wait for user input before exiting
        Console.ReadLine();
    }

    static string FormatTimeSpent(int timeSpentSeconds)
    {
        int hours = timeSpentSeconds / 3600; // Number of whole hours
        int minutes = (timeSpentSeconds % 3600) / 60; // Number of whole minutes

        string formattedTimeSpent = $"{hours}h{minutes.ToString().PadLeft(2, '0')}m";
        return formattedTimeSpent;
    }

    static async Task<List<Project>> GetProjectsForUser(HttpClient client, int userId)
    {
        const int perPage = 100; // Number of items to list per page (max: 100)
        int page = 1; // Start with the first page

        List<Project> projects = new List<Project>();

        while (true)
        {
            // Send a GET request to the API to retrieve projects for the user
            HttpResponseMessage response = await client.GetAsync($"projects?page={page}&per_page={perPage}");

            // Check if the request was successful
            response.EnsureSuccessStatusCode();

            // Read the response content as a JSON string
            string responseContent = await response.Content.ReadAsStringAsync();

            // Parse the JSON string into a list of projects
            List<Project> pageProjects = JsonConvert.DeserializeObject<List<Project>>(responseContent);

            // Add the projects from the current page to the overall projects list
            projects.AddRange(pageProjects);

            // If the current page has fewer projects than the requested perPage, we have reached the last page
            if (pageProjects.Count < perPage)
            {
                break;
            }

            // Move to the next page
            page++;
        }

        return projects;
    }

    static async Task<Tuple<int, Dictionary<int, Tuple<string, int>>>> GetTimeSpentForProject(HttpClient client, int projectId, int userId)
    {
        // Send a GET request to the API to retrieve the time spent on the project by the user
        HttpResponseMessage response = await client.GetAsync($"projects/{projectId}/issues?assignee_id={userId}");
        Dictionary<int, Tuple<string, int>> _issueId_Description_TotalTime = new Dictionary<int, Tuple<string, int>>();

        // Check if the request was successful
        response.EnsureSuccessStatusCode();

        string responseContent = await response.Content.ReadAsStringAsync();
        //Console.WriteLine(responseContent); 

        // Parse the JSON string into a JArray
        JArray issuesArray = JArray.Parse(responseContent);

        // Calculate the total time spent across all issues
        int totalTimeSpent = 0;
        foreach (JToken issueToken in issuesArray)
        {
            JObject issueObject = (JObject)issueToken;
            //Console.WriteLine(issueObject);
            JToken timeSpentToken = issueObject["time_stats"]?["total_time_spent"];
            JToken issueId = issueObject["iid"];
            JToken description = issueObject["title"];
            //JToken label = issueObject["labels"];

            if (timeSpentToken != null && timeSpentToken.Type == JTokenType.Integer)
            {
                //Console.WriteLine(timeSpentToken);
                totalTimeSpent += (int)timeSpentToken;
            }

            if (issueId != null && issueId.Type == JTokenType.Integer)
            {

                var descr = string.Empty;

                if (description != null && description.Type == JTokenType.String)
                {
                    descr = description.ToString();
                }

                //if (label != null)
                //{
                //    var labels = label.ToString().Split(',');
                //    descr += " | " + string.Join(";", labels);
                //}

                if (_issueId_Description_TotalTime.ContainsKey((int)issueId))
                {
                    _issueId_Description_TotalTime[(int)issueId] = new Tuple<string, int>(descr, (int)timeSpentToken + _issueId_Description_TotalTime[(int)issueId].Item2);
                }
                else
                {
                    _issueId_Description_TotalTime.Add((int)issueId, new Tuple<string, int>(descr, (int)timeSpentToken));
                }
            }
            
        }

        return new Tuple<int, Dictionary<int, Tuple<string, int>>>(totalTimeSpent, _issueId_Description_TotalTime);
    }
}

class Project
{
    public int Id { get; set; }
    public string Name { get; set; }
}

class Issue
{
    public TimeStats TimeStats { get; set; }
}

class TimeStats
{
    public int TotalTimeSpent { get; set; }

    public int HumanTotalTimeSpent { get; set; }
}
