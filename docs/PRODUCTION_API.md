# Central API deployment

Production branch uses https://cropcareapi-production.up.railway.app/ via CropCareApi:BaseUrl. Override through CropCareApi__BaseUrl. Existing MVC/Razor UI and server-side authenticated API calls remain. Firebase REST exchanges custom tokens from API username login; secure authentication cookies and the server token store are retained. Browser clients do not receive Admin credentials.

Configure the existing Firebase:WebApiKey separately for Firebase REST sign-in. Keep one Web replica for the in-memory session store; process restart requires sign-in again. Username login requires the API's reviewed Firestore registry. Database migrations/rules are unchanged. See the API repository docs/RAILWAY_DEPLOYMENT.md and docs/VERIFICATION.md for deployment and acceptance results.
