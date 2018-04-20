# Octopus Webhooks

Via the subscriptions listed in `EventSubscriptions.json`, respond to the configured events by deploying to the given tenants.  Notifies via email or Slack on failure.

This is a [Freya](https://docs.freya.io/en/latest/index.html) server written in F# that runs on http://localhost:8080/deploy by default.  In Octopus, use Configuration | Subscriptions to send any combination of events you prefer to the Freya server.

The structure of EventSubscriptions.json follows.  All `Name` elements are used simply to make the JSON readable.  `Id` elements are the Octopus IDs, discoverable via their API.  The included file is only an example, and will need to be configured for your use.

- `Subscriptions`: Array of events to which the server should respond, along with their deployment schemes 
    - `Events`: Array of events
        - `Category`: Category of the event coming from Octopus, i.e., "Deployment Succeeded"
        - `Channel`: Channel on which the event occurred
            - `Name`
            - `Id`
        - `Enabled`: Toggles this trigger
        - `Notify`: Notification settings for failuresm
            - `Emails`: Array of email addresses
            - `Slack`: Array of Slack channels to notify on failure
                - `Name`: Channel name
                - `Url`: Slack API URL for the channel
        - `Project`: Project for which this event occurred
            - `Name`
            - `Id`
        - `Tenant`: Tenant on which this event occurred
            - `Name`
            - `Id`
    - `DeploymentSchemes`: Array of environments/tenants to which to fire deployments on the configured events
        - `Environment`: Environment to which to deploy
            - `Name`
            - `Id`
            - `Tenants`: Array of tenants to which to deploy
                - `Name`
                - `Id`

**Service installation:**
```sh
OctopusWebhooks.exe install -displayname "Octopus Webhooks" -servicename "OctopusWebhooks" --localservice --autostart -description "Octopus event subscriptions and their desired targets"
```

**Service uninstallation:**
```sh
OctopusWebhooks.exe uninstall -servicename OctopusWebhooks -instance OctopusWebhooks
```


NOTE: In `Api.fs`:
- Ensure that the type providers point to accessible URLs
- `octopusAPIKey` is set to a valid key
- `octopusRoot` is set to your Octopus server's URL
- The from address in `msg` is set
- The mail server in `client` is set

This repository is made available with no warranties as to performance, applicability, or reliability.