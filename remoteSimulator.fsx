open System.Threading
#r "nuget: Akka.FSharp" 
#r "nuget: Akka.Remote"
#load "MessageTypes.fsx"
open System
open Akka.Actor
open Akka.Configuration
open Akka.FSharp

open MessageTypes.Messages

let mutable followerMapSimulator: Map<string, list<string>> = Map.empty
let mutable followingMapSimulator: Map<string, list<string>> = Map.empty
let mutable allUserSetSimulator: Set<string> = Set.empty
let mutable mapUserNametoTweets: Map<String, list<string>> = Map.empty
let stopwatch = System.Diagnostics.Stopwatch()
let mutable totalRequests: int = 0
let mutable totalSimulatedTweets : int = 0
let config =
    Configuration.parse
        @"akka {
            actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            remote.helios.tcp {
                hostname = localhost
                port = 9001
            }
        }"



let system = System.create "RemoteFSharp" config



let getActorRef (userId: string) =
    let userActorRef = system.ActorSelection("akka.tcp://RemoteFSharp@localhost:60143/user/" + userId)
    userActorRef

stopwatch.Start()
let mutable supervisoRef:IActorRef = null

let echoServer = 
    spawn system "server"
    <| fun mailbox ->
        let rec loop() =
            actor {
                
                if(stopwatch.ElapsedMilliseconds > 120000L) then
                    supervisoRef <! 1
                    printfn "Total requests processes: %A" totalRequests
                    printfn "Total tweets sent: %A\n\n" totalSimulatedTweets
                    Thread.Sleep(1200000)
                    system.Terminate() |> ignore
                
                let! msg = mailbox.Receive()
                let sender = mailbox.Sender()
                totalRequests <- totalRequests + 1                
                match box msg with
                | :? (string) as cmd->
                    printfn  "Total Requests processed are %i" totalRequests
                    printfn "Total Tweets are %i" totalSimulatedTweets
                    sender <! "DONE"
                    system.Terminate() |> ignore

                | :? Register as input ->
                    let allUsers = input.UserId
                    for users in [1..allUsers] do
                        let user = "username" + users.ToString()
                        allUserSetSimulator <- allUserSetSimulator.Add(user)
                        followerMapSimulator <- followerMapSimulator.Add(user, list.Empty)
                        followingMapSimulator <- followingMapSimulator.Add(user, list.Empty)

                    supervisoRef <- sender

                
                | :? SimulateFollowers as input->
                    let userId = input.UserId
                    let followersList = input.FollowersList
                    for listItem in followersList do
                        if((allUserSetSimulator.Contains(listItem)) && allUserSetSimulator.Contains(userId)) then
                            let found = followerMapSimulator.TryFind(userId)
                            match found with
                            | Some currentList ->
                                let mutable temp = currentList
                                temp <- temp @ [ listItem ]
                                followerMapSimulator <- followerMapSimulator.Add(userId, temp)
                                ()
                            | None -> printfn "UserId not found"

                            let found2 = followingMapSimulator.TryFind(listItem) 
                            match found2 with
                            | Some currentList ->
                                let mutable temp1 = currentList
                                temp1 <- temp1 @ [userId]
                                followingMapSimulator<- followingMapSimulator.Add(listItem, temp1)

                            | None -> printfn "ListItem not found"

                | :? SendToServer as input->
                    let userId = input.UserId
                    let tweet = input.Tweet
                    let found = followerMapSimulator.TryFind(userId)
                    match found with
                    | Some x->
                        for oneUser in x do
                            let toUserActorRef = getActorRef oneUser
                            let sendTweet : SendTweet = {
                                Tweet = tweet;
                                SenderUser = userId;
                            }
                            totalSimulatedTweets <- totalSimulatedTweets + 1
                            toUserActorRef <! sendTweet
                            
                    | None -> printfn "Does not exis!"


                | :? SendRetweet as input ->
                    let tweet = input.Tweet
                    let senderUser = input.ClientId
                    let originalTweeter = input.ReTweetUser

                    let found = followerMapSimulator.TryFind(senderUser)
                    match found with
                        | Some followersList ->
                            for oneUser in followersList do
                                let toUserActorRef = getActorRef oneUser
                                let senderTweet : SendTweet = {
                                    Tweet = tweet;
                                    SenderUser = senderUser
                                }
                                totalSimulatedTweets <- totalSimulatedTweets + 1
                                toUserActorRef <! senderTweet    
                            
                        | None -> printfn "Invalid" 


                | _ -> printfn "Invalid Command"


                return! loop ()

            }
        loop()

Console.ReadLine() |> ignore
system.Terminate() |> ignore