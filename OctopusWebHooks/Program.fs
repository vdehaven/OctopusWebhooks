namespace DevOps.Octopus.WebHooks

open Freya.Core
open Suave
open Topshelf
open System.Threading
open Suave.Web  

// Start a Suave server to service webhooks inbound from Octopus.  See: https://octopusserver.com/app#/configuration/subscriptions
// Wrap as a Windows service via Topshelf.

module Program =

    [<EntryPoint>]
    //let main argv =
    let main _ =
        // let config = defaultConfig
        // let config = match argv.Length with
        //              | 1 -> { config with bindings = [ HttpBinding.createSimple HTTP "192.168.1.237" (int argv.[0]) ]}
        //              | _ -> config
        // startWebServer
        //     config
        //     (Owin.OwinApp.ofAppFunc "/" (OwinAppFunc.ofFreya Api.root))

        //startWebServer
        //    defaultConfig
        //    //(Owin.OwinApp.ofAppFunc "/" (OwinAppFunc.ofFreya OctopusWebHooks.Api.root )) //Api.root))
        //    //(Owin.OwinApp.ofAppFunc "/" (OwinAppFunc.ofFreya DevOps.Octopus.WebHooks.Api.root))
        //    (Owin.OwinApp.ofAppFunc "/" (OwinAppFunc.ofFreya Api.root))

        //0
        let cancellationTokenSource = ref None

        // Service start
        let start hc = 
            let cts = new CancellationTokenSource()
            let token = cts.Token
            let config = { defaultConfig with cancellationToken = token}

            startWebServerAsync config (Owin.OwinApp.ofAppFunc "/" (OwinAppFunc.ofFreya Api.root)) // Hook up Api.fs for request routing
            |> snd
            |> Async.StartAsTask 
            |> ignore

            cancellationTokenSource := Some cts
            true

        // Service stop
        let stop hc = 
            match !cancellationTokenSource with
            | Some cts -> cts.Cancel()
            | None -> ()
            true

        // Service configuration
        Service.Default 
        |> display_name "Octopus Webhooks"
        |> instance_name "OctopusWebhooks"
        |> description "MyCompany-specific custom webhooks subscribed to in Octopus"
        |> with_start start
        |> with_stop stop
        |> run