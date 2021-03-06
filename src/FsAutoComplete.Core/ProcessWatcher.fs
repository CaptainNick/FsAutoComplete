namespace FsAutoComplete

open System.Diagnostics
open System

module ProcessWatcher =

  type private OnExitMessage =
    | Watch of Process * (Process -> unit)

  let private watcher = new MailboxProcessor<OnExitMessage>(fun inbox ->
    let rec loop underWatch =
        async {
            let! message = inbox.TryReceive(System.TimeSpan.FromSeconds(0.5).TotalMilliseconds |> int)
            let next =
                match message with
                | Some (Watch (proc, a)) ->
                    (proc, a) :: underWatch
                | None ->
                    let exited, alive = underWatch |> List.partition (fun (p, _) -> p.HasExited)
                    exited |> List.iter (fun (p,a) -> a p)
                    alive
            do! loop next
        }
    loop [] )

  let watch proc onExitCallback =
    if Utils.runningOnMono then
        watcher.Start()
        watcher.Post (OnExitMessage.Watch(proc, onExitCallback))
    else
        proc.EnableRaisingEvents <- true
        proc.Exited |> Event.add (fun _ -> onExitCallback proc)

  let zombieCheckWithHostPID quit pid =
    try
      let hostProcess = System.Diagnostics.Process.GetProcessById(pid)
      watch hostProcess (fun _ -> quit ())
    with
    | e ->
      Debug.print "[Process Watcher] Host process ID %i not found: %s" pid e.Message
      // If the process dies before we get here then request shutdown
      // immediately
      quit ()
