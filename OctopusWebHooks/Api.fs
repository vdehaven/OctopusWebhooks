// Respond to Octopus event subscriptions in EventSubscriptions.json and deploy to their desired targets.
namespace DevOps.Octopus.WebHooks

open FSharp.Data

exception InvalidEnvironmentError of string // If the webhook fired from an unexpected environment...

type DeployHook = JsonProvider<"OctopusDeploymentWebhookSample.json">
type DeployPost = JsonProvider<"OctopusDeploymentPostSample.json">
type Subscriptions = JsonProvider<"EventSubscriptions.json">

module Api = 
    open Freya.Core
    open Freya.Machines.Http
    open Freya.Types.Http
    open Freya.Routers.Uri.Template
    open FSharp.Core
    open System.IO
    open System.Net.Mail
    open System.Runtime.ExceptionServices
    open System.Threading

    let octopusRoot = "https://octopusserver.com"
    let deployURL = sprintf "%s/api/deployments" octopusRoot // Fire deployments through this Octopus API endpoint.
    let octopusAPIKey = "" // API key for the account, used to authenticate to Octopus
    let deployHeaders = [ "Content-Type", "application/json"; HttpRequestHeaders.Accept HttpContentTypes.Json; "X-Octopus-ApiKey", octopusAPIKey ] // HTTP headers for the request
    let subscriptions = Subscriptions.GetSample().Subscriptions // Load from config file: EventSubscriptions.json

    // Pull a string from a Stream, stripping quotes.
    let grabString (bodyStream : #Stream ) = 
        use reader = new StreamReader(bodyStream)
        match reader.ReadToEnd() with
        | str when str.[0] = '"' && str.[str.Length - 1] = '"' ->
            Some <| str.Substring(1, str.Length - 2)
        | str -> Some str
        | _ -> None

    let method_ = Freya.Optics.Http.Request.method_

    // Parse data POSTed from Octopus using OctopusDeploymentWebhookSample.json template.
    let posted = 
        freya {
            let! requestMethod = Freya.Optic.get method_
            let! postedWorking =
                match requestMethod with
                | POST -> Freya.Optic.get Freya.Optics.Http.Request.body_ |> Freya.map grabString
                | _ -> Freya.init None
        
            match postedWorking with
            | Some p -> return DeployHook.Parse p
            | None -> return DeployHook.GetSample() }

    // Build Octopus deployment request and asynchronously fire it.  Email and Slack messages are sent on failure.
    let deployAsync releaseID (environment:Subscriptions.Environment) (tenant:Subscriptions.Channel) eventMessage (event:Subscriptions.Event) =
        let deployText = sprintf """{
                                        "ReleaseId": "%s",
                                        "EnvironmentId": "%s",
                                        "TenantId": "%s",
                                        "SkipActions": [],
                                        "QueueTime": null,
                                        "QueueTimeExpiry": null,
                                        "FormValues": {},
                                        "ForcePackageDownload": false,
                                        "UseGuidedFailure": false,
                                        "SpecificMachineIds": [],
                                        "ExcludedMachineIds": [],
                                        "ForcePackageRedeployment": false
                                    }""" releaseID environment.Id tenant.Id
        let deployBody = TextRequest deployText

        // Asynchronously fire the deployment, notifying on failure via email and Slack those configured in the subscription.
        async {
            let! deployResult = Async.Catch (Http.AsyncRequestString(deployURL, httpMethod = "POST", body = deployBody, headers = deployHeaders))
            match deployResult with
            | Choice1Of2 success -> return success
            | Choice2Of2 ex ->
                let exnCapture = ExceptionDispatchInfo.Capture ex
                match ex with
                | :? System.Net.WebException ->
                    let releaseLink = sprintf "%s/app#/releases/%s" octopusRoot releaseID
                    let messageSubject = sprintf "Deployment of %s to %s failed" event.Project.Name tenant.Name
                
                    // Email on failure.
                    use msg = new MailMessage(@"devops@mycompany.com",
                                            String.concat "," event.Notify.Emails,
                                            messageSubject, //sprintf "%s %s deployment failed" event.Project.Name tenant.Name,
                                            sprintf "Triggering event: %s\nDetails can be found here: %s" eventMessage releaseLink)
                    use client = new SmtpClient(@"mailserver.mycompany.com")
                    client.DeliveryMethod <- SmtpDeliveryMethod.Network
                    client.Send msg
                
                    // Slack it too.
                    event.Notify.Slack 
                    |> Array.map (fun slack -> let slackText = sprintf "%s\nTriggering event: %s\n<%s|Click here> for details." messageSubject eventMessage releaseLink
                                               let slackBody = TextRequest (sprintf """{"text": "%s"}""" slackText)
                                               Http.AsyncRequestString(slack.Url, httpMethod = "POST", headers = deployHeaders, body = slackBody, silentHttpErrors = true, timeout = 30))
                    |> Async.Parallel
                    |> Async.Ignore // No guaranteed delivery of these notifications.
                    |> Async.RunSynchronously
                | _ -> ()
                
                exnCapture.Throw() // Re-throw the captured error for cancellation purposes upstream.
                return (sprintf "Error: %s" ex.Message) }
    
    // Just reorders Async.RunSynchronously so we may pipe to it using a timeout and a cancellation token.
    let cancellableAsync timeout token asyncArray:Choice<string[],exn> = Async.RunSynchronously(asyncArray, timeout, token)
    
    // Given a deployment scheme, parallel the deployment calls.  Cancel the deployments on error.
    let deployToScheme (scheme:Subscriptions.DeploymentScheme) releaseID eventMessage (event:Subscriptions.Event) =
        let cancellationSource = new CancellationTokenSource()
        let deployResult = scheme.Environment.Tenants |> Array.map (fun tenant -> deployAsync releaseID scheme.Environment tenant eventMessage event)
                                                      |> Async.Parallel
                                                      |> Async.Catch
                                                      |> cancellableAsync 30 (cancellationSource.Token)
        
        match deployResult with
        | Choice1Of2 _ -> ()
        | Choice2Of2 ex ->
            let exnCapture = ExceptionDispatchInfo.Capture ex
            match ex with
            | :? System.Net.WebException -> cancellationSource.Cancel();() // Cancel the set of deployments.  Error has already been handled.
            | _ -> exnCapture.Throw();()

    // Extract the release ID from payload and deploy the associated release to the configured targets from EventSubscriptions.json.
    let deploy = 
        freya {
            let! posted = posted
            let releaseID = posted.Payload.Event.RelatedDocumentIds |> Array.find (fun q -> q.StartsWith "Releases")
            //let environmentID = posted.Payload.Event.RelatedDocumentIds |> Array.find (fun q -> q.StartsWith "Environments")
            let channelID = posted.Payload.Event.RelatedDocumentIds |> Array.find (fun q -> q.StartsWith "Channels")
            let category = posted.Payload.Event.Category
            let eventMessage = posted.Payload.Event.Message
            let projectID = posted.Payload.Event.RelatedDocumentIds |> Array.find (fun q -> q.StartsWith "Projects")
            let tenantID = posted.Payload.Event.RelatedDocumentIds |> Array.find (fun q -> q.StartsWith "Tenants")
            let events = subscriptions |> Array.collect (fun p -> p.Events |> Array.filter (fun q -> q.Category.Equals category &&
                                                                                                     q.Channel.Id = channelID &&
                                                                                                     q.Enabled = true &&
                                                                                                     q.Project.Id = projectID &&
                                                                                                     q.Tenant.Id = tenantID))

            if events.Length > 0 then
                let event = events.[0] // By design, only consider the first match.  There should only be one provided in EventSubscriptions.json, but it's not forced.
                let targets = subscriptions |> Array.filter (fun p -> p.Events |> Array.contains event)
                targets |> Seq.iter (fun target -> target.DeploymentSchemes
                                                   |> Array.iter (fun scheme -> deployToScheme scheme releaseID eventMessage event))
            else () }

    // "/deploy" route points to this machine.  Accept POSTed JSON data from Octopus, orchestrate deployment of the associated release to the configured target in EventSubscriptions.json.
    let deployMachine =
        freyaMachine {
            methods [POST]
            availableMediaTypes [MediaType.Json]
            doPost deploy }
            //handleOk displayRaw }

    // Router
    let root =
        freyaRouter {
            //resource "/hook{/id}" hookMachine // example for future hooks
            resource "/deploy" deployMachine }
