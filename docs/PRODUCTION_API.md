# Central API deployment

Production branch uses https://cropcareapi-production.up.railway.app/ via CropCareApi:BaseUrl. Override through CropCareApi__BaseUrl. Existing MVC/Razor UI and server-side authenticated API calls remain. Firebase REST exchanges custom tokens from API username login; secure authentication cookies and the server token store are retained. Browser clients do not receive Admin credentials.

Configure the existing Firebase:WebApiKey separately for Firebase REST sign-in. Keep one Web replica for the in-memory session store; process restart requires sign-in again. Username login requires the API's reviewed Firestore registry. Database migrations/rules are unchanged. See the API repository docs/RAILWAY_DEPLOYMENT.md and docs/VERIFICATION.md for deployment and acceptance results.

Local login configuration: the client Firebase API key must be in the Web project's User Secrets (Firebase:WebApiKey) or environment (Firebase__WebApiKey). Git pull does not transfer User Secrets. The user's running checkout is D:\VS CODES\Stigma424\CropCare.Web. Missing configuration now produces a friendly login error instead of an unhandled exception. Recovery recognizes deliveryMode=firebase_link and directs users to their email/reset link, preserving the existing six-digit-code workflow for SMTP mode.
