It would be awesome if we could make a version of Gorillas.BAS as a modern .NET Blazor app with online multiplayer

The original source code is at https://raw.githubusercontent.com/SWO-GS/gorillas/refs/heads/master/gorillas.bas

For an initial implementation, we can have it be a blazor app where people take turns.

But after that, we would want to have;

 - A way to have two players on different computers, where the server stores the information on the game.
   - We can use SQLite to store state
     - We should use Linq2Db for the data access where appropriate 
   - We can use SignalR for real-time communication between clients and the server.
   - We should consider using Akka.NET for handling concurrency and replay of events when someone logs in after a disconnect
     - This also gives us a 'session replay' feature to re-play games after they have finished.
     - We should use Akka.Persistence.Sql for this, it also uses linq2db for data access.
   - A way to have observers
 - A way to have a single player play against a computer.
