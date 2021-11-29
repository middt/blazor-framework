using Microsoft.AspNetCore.SignalR.Client;
using System;


namespace Test.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            string hubUrl = "http://127.0.0.1:7010/hubs/testhub";

            HubConnection connection = new HubConnectionBuilder()
                .WithUrl(new Uri(hubUrl))
                .WithAutomaticReconnect()
                .Build();
            
            connection.StartAsync().Wait();

            connection.On<string>("Receive", (result)=>
            {
                System.Console.WriteLine(result);
            
            });

            System.Console.ReadLine();
        }
    }
}
