## Privacy Policy

UnoVibe currently stores the following information on the user's device:

* General settings of the application.
* Recently opened folder paths, so UnoVibe can provide its recent-folder functionality.
* OpenCode server endpoints that the user has connected to.
* Whether authentication is required for a saved endpoint.
* Optionally, an OpenCode server password, but only when the user explicitly chooses to save it.
* AI-provider credentials (API keys or OAuth tokens) entered through the "Connect a provider" dialog. UnoVibe does not store these itself; it sends them to the connected OpenCode server, which stores them on its side (for a locally launched server, that is on the user's own machine). When connecting to a remote server, the credential is transmitted to that server, so only connect to servers you trust.

If a user explicitly chooses to save an OpenCode server password, UnoVibe stores the password locally in plaintext. UnoVibe displays a confirmation before enabling this option to ensure that the user understands the security implications. Users should only enable password storage on devices they trust and understand that other users or software with access to the relevant local storage may be able to obtain the saved password.

Provider OAuth flows are completed by the OpenCode server against the AI provider. UnoVibe only opens the provider's authorization page in the user's browser and, for headless flows that require one, relays the authorization code the user pastes back to the server; it never sees the resulting access token.

UnoVibe communicates with OpenCode servers and does not determine how OpenCode or AI providers process information submitted through those servers. Information sent through OpenCode may be transmitted to the AI provider selected by the user. Users should review the [OpenCode Privacy Policy](https://opencode.ai/legal/privacy-policy) and the privacy/data-use policies of their selected AI providers before submitting sensitive information.

UnoVibe currently does not operate a cloud service for storing user folders, OpenCode endpoints, passwords, prompts, source code, or AI conversations.
