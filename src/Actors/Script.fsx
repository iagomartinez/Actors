// Learn more about F# at http://fsharp.net. See the 'F# Tutorial' project
// for more guidance on F# programming.
#r @"..\packages\Akka.1.0.0\lib\net45\Akka.dll"
#r @"..\packages\Akka.FSharp.1.0.0\lib\net45\Akka.FSharp.dll"
#r @"C:\code\Actors\packages\FsPickler.0.9.11\lib\net45\FsPickler.dll"

#load "Library1.fs"
open Actors
open Akka
open Akka.FSharp

type PlayerNumber =
| One
| Two
| Unknown

type PlayerDetails = {
    name : string
    handicap : int
    number : PlayerNumber
}

type Play =
| Ping of Akka.Actor.IActorRef
| Pong of Akka.Actor.IActorRef
| Serve of Akka.Actor.IActorRef

type LostPlay = 
| MissedShot of PlayerNumber

type Score = 
| Point of PlayerNumber

type PlayerEvent = 
| Registration of PlayerDetails

type GameEvent =
| NewGame
| GameOver

type PlayerState =
| Playing
| NotPlaying

let randomIndex () = 
    let r = System.Random()
    r.Next() % 100

let randomIndex2 () = 
    let r = System.Random(100)
    r.Next()


let generateDistribution (n : int) (threshold : int) = 
    seq {
        let r = System.Random()
        for i in 1..n do            
            let ri = r.Next() % 100
            yield ri < threshold
    }
        
generateDistribution 10000 70
|> Seq.fold (fun (t,f) next -> if next then (t+1,f) else (t,f+1)) (0,0)
|> fun (t,f) -> printfn "true: %i, false: %i" t f


let playerActor (mailbox : Actor<'a>) =
    let rec loop (details : PlayerDetails) (state : PlayerState) = actor {
        let handlePlay (msg : obj)= 
            match msg with
            | :? Play as m -> 
                match m with
                | Ping opponent -> 
                    let r = randomIndex()
                    if r < details.handicap then
                        mailbox.Sender() <! Play.Pong(mailbox.Self)
                    else
                        printfn "crap! I, %s missed." details.name
                        publish (LostPlay.MissedShot(details.number)) mailbox.Context.System.EventStream
                | Pong opponent-> 
                    printfn "I, %s, got a Pong from %A! Ping!" details.name opponent
                    let r = randomIndex()
                    if r < details.handicap then
                        mailbox.Sender() <! Play.Ping(mailbox.Self)
                    else
                        printfn "crap! I, %s missed." details.name
                        publish (LostPlay.MissedShot(details.number)) mailbox.Context.System.EventStream
                | Serve opponent -> opponent <! Play.Ping(mailbox.Self)                            
                | _ -> printfn "WTF do i do with this %A" (msg)
            | _ -> printfn "WTF do i do with this %A" (msg)                                    

        let! (msg : obj) = mailbox.Receive()
        printfn "I, %s received a message" details.name
        match state, msg with
        | NotPlaying, (:? PlayerEvent as g) ->  
                match g with
                | Registration details -> 
                    printfn "Game on!"
                    return! loop details PlayerState.Playing

        | NotPlaying, _ ->
            printfn "Hey I'm not playing!"
            return! loop details state
        | Playing, (:? GameEvent as g) -> 
                match g with
                | GameOver -> 
                    printfn "OK game over"
                    return! loop details PlayerState.NotPlaying
                | _ -> ()
        | Playing, _ -> 
            handlePlay msg
            return! loop details state        
    }
    let eventStream = mailbox.Context.System.EventStream
    subscribe typeof<GameEvent> mailbox.Self eventStream |> ignore
    loop {name = "anonymous"; handicap = 0; number = Unknown} PlayerState.NotPlaying    

let scoreboardActor (mailbox : Actor<'A>) =
    let rec loop (p1:int,p2:int) = actor {
        let! (msg : obj) = mailbox.Receive()

        match msg with
        | :? Score as s -> 
            match s with
            | Point player -> 
                printfn "player %A scores!" player
                match player with 
                | One -> return! loop(p1+1,p2)
                | Two -> return! loop(p1,p2+1)
            | _ -> ()
        | :? GameEvent as g ->
            match g with
            | GameOver -> 
                printfn "Game over! final scores: %i - %i" p1 p2
                return! loop(p1,p2)
            | NewGame -> 
                printfn "New game!"
                return! loop(0,0)
        | _ -> 
            printfn "ignoring random message, i only listen for Score, sorry :p"
            return! loop(p1,p2)
    }    
    let eventStream = mailbox.Context.System.EventStream
    subscribe typeof<Score> mailbox.Self eventStream |> ignore
    subscribe typeof<GameEvent> mailbox.Self eventStream |> ignore
    loop (0,0)

let umpireActor (mailbox : Actor<'a>) =
    let rec loop () = actor {
        let! (msg : obj) = mailbox.Receive()
        match msg with 
        | :? LostPlay as l -> 
            match l with
            | MissedShot p -> if p = PlayerNumber.One then publish (Score.Point(PlayerNumber.Two)) mailbox.Context.System.EventStream else publish (Score.Point(PlayerNumber.One)) mailbox.Context.System.EventStream
            | _ -> ()                        
        return! loop()
    }
    let eventStream = mailbox.Context.System.EventStream
    subscribe typeof<LostPlay> mailbox.Self eventStream |> ignore        
    loop()

let gameHandler (mailbox : Actor<'a>) (msg : obj) = 
    match msg with
    | :? GameEvent as g ->
        match g with
        | GameOver  -> publish msg mailbox.Context.System.EventStream
        | NewGame   -> publish msg mailbox.Context.System.EventStream
        | _ -> printfn "WTF do I do with this %A" msg
    | _ -> printfn "WTF do I do with this %A" msg



let system = System.create "PingPong" (Akka.FSharp.Configuration.defaultConfig())

let game = spawn system "game3" (actorOf2 gameHandler)
let ref = spawn system "ref" umpireActor
let scoreboard = spawn system "scoreboard10" scoreboardActor
let p1 = spawn system "pete23" playerActor
let p2 = spawn system "andy23" playerActor

//scoreboard <! "bla"

game <! GameEvent.NewGame

//p1 <! GameEvent.StartGame(p2,{name="andy"; handicap=50; number=One})
//p2 <! GameEvent.StartGame(p1,{name="pete"; handicap=50; number=Two})
//scoreboard <! (GameEvent.NewGame)

p1 <! (Play.Serve(p2))
p2 <! (Play.Serve(p1))
p1 <! (Play.Serve(p2))
p2 <! (Play.Serve(p1))

game <! GameEvent.GameOver

//p1 <! (Play.Ping)
//p2 <! (Play.Ping)
//p1 <! (Play.Pong)

//p1 <! (GameEvent.GameOver)
//p2 <! (GameEvent.GameOver)
//scoreboard <! (GameEvent.GameOver)

//  TODO: 
//  7. player register for a game
//  2. refactor scoreboard to accept Ask message for score
//  3. Have ref keep watch on scores and judge when game is over (first to 11 or something)
//  4. replace StartGame, Register with Game object instead
//  5. ideally, encapsulate player details in the actor somehow... how would I "close" details in actor?
//  6. what about supervisor??

// DONE
//  1. refactor GameOver to be an event that players and scoreboard listens to