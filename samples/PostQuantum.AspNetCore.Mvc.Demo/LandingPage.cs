namespace PostQuantum.AspNetCore.Mvc.Demo;

internal static class LandingPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>PostQuantum.AspNetCore MVC demo</title>
  <style>
    body { font-family: system-ui, sans-serif; max-width: 760px; margin: 2rem auto; padding: 0 1rem; }
    code { background:#f4f4f4; padding:2px 5px; border-radius:3px; }
    pre { background:#f7f7f7; padding:0.75rem; border-radius:4px; white-space:pre-wrap; }
    button { font: inherit; margin: 0.25rem 0.25rem 0.25rem 0; }
    .row { display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap; margin: 0.5rem 0; }
    .row label { min-width: 60px; }
    input { font: inherit; }
    #out { background:#f7f7f7; padding:0.75rem; border-radius:4px; max-height: 240px; overflow:auto; white-space:pre-wrap; }
  </style>
</head>
<body>
  <h1>PostQuantum.AspNetCore MVC demo</h1>
  <p>Classic controller-based ASP.NET Core MVC with post-quantum JWT
     authentication. Each endpoint below uses a different authorization
     pattern; the buttons mint a token with the right claims, then call.</p>

  <h2>1. Mint a token</h2>
  <div class="row">
    <label>user:</label> <input id="user" value="alice">
    <label>role:</label> <input id="role" value="admin">
    <label>tenant:</label> <input id="tenant" value="acme">
    <button onclick="mintToken()">Mint</button>
  </div>
  <pre id="tokenPreview">(no token yet)</pre>

  <h2>2. Hit endpoints</h2>
  <p>
    <button onclick="call('/me')">GET /me ([Authorize])</button>
    <button onclick="call('/admin/health')">GET /admin/health ([Authorize(Roles="admin")])</button>
    <button onclick="call('/acme/dashboard')">GET /acme/dashboard ([Authorize(Policy="AcmeTenant")])</button>
    <button onclick="call('/me', false)">GET /me (no token, expect 401)</button>
  </p>

  <h2>3. Response</h2>
  <pre id="out"></pre>

  <script>
    let token = null;
    const out = document.getElementById('out');

    async function mintToken() {
      const u = document.getElementById('user').value;
      const r = document.getElementById('role').value;
      const t = document.getElementById('tenant').value;
      const url = `/dev/token?user=${encodeURIComponent(u)}&role=${encodeURIComponent(r)}&tenant=${encodeURIComponent(t)}`;
      const resp = await fetch(url, { method: 'POST' });
      const json = await resp.json();
      token = json.token;
      document.getElementById('tokenPreview').textContent =
        token.slice(0, 80) + '...  (' + token.length + ' chars)';
      out.textContent = `Token minted (${token.length} chars).\n`;
    }

    async function call(path, withToken = true) {
      if (withToken && !token) {
        out.textContent = 'Mint a token first.';
        return;
      }
      const headers = withToken ? { 'Authorization': 'Bearer ' + token } : {};
      const resp = await fetch(path, { headers });
      let body;
      try { body = JSON.stringify(await resp.json(), null, 2); }
      catch { body = await resp.text(); }
      out.textContent = `${resp.status} ${resp.statusText}\n\n${body}`;
    }
  </script>
</body>
</html>
""";
}
