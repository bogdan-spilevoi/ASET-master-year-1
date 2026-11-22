using System.Net;

namespace SmartLost.AuthService.Api.Pages;

public static class EmailConfirmationPageRenderer
{
    public static string RenderConfirmationPage(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        string encodedToken = WebUtility.HtmlEncode(token.Trim());

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <meta name="referrer" content="no-referrer" />
    <title>Confirm your SmartLost email address</title>
    <style>
        body {
            font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif;
            margin: 0;
            padding: 2rem 1rem;
            background: #f5f7fb;
            color: #162032;
        }
        main {
            max-width: 28rem;
            margin: 3rem auto;
            padding: 2rem;
            background: #ffffff;
            border: 1px solid #dce3ee;
            border-radius: 0.75rem;
            box-shadow: 0 8px 24px rgba(16, 32, 50, 0.06);
        }
        h1 {
            font-size: 1.35rem;
            margin: 0 0 0.75rem;
        }
        p {
            line-height: 1.5;
            margin: 0 0 1.25rem;
        }
        button {
            appearance: none;
            border: 0;
            border-radius: 0.5rem;
            padding: 0.75rem 1.25rem;
            font-size: 1rem;
            font-weight: 600;
            color: #ffffff;
            background: #175cd3;
            cursor: pointer;
        }
        button:disabled {
            background: #98a2b3;
            cursor: default;
        }
        #status {
            margin: 1rem 0 0;
            font-weight: 500;
        }
        .success { color: #067647; }
        .error { color: #b42318; }
    </style>
</head>
<body>
    <main>
        <h1>Confirm your SmartLost email</h1>
        <p>Select the button below to confirm your email address. Opening this page alone does not activate the confirmation link.</p>
        <form id="confirm-form" method="post" action="confirm-email">
            <input type="hidden" id="confirmation-token" name="token" value="{{encodedToken}}" />
            <button type="submit" id="confirm-button">Confirm email</button>
        </form>
        <p id="status" role="status" aria-live="polite"></p>
    </main>
    <script>
        (function () {
            const form = document.getElementById("confirm-form");
            const tokenInput = document.getElementById("confirmation-token");
            const button = document.getElementById("confirm-button");
            const status = document.getElementById("status");

            form.addEventListener("submit", async function (event) {
                event.preventDefault();
                button.disabled = true;
                status.className = "";
                status.textContent = "Confirming your email address...";

                try {
                    const response = await fetch(window.location.pathname, {
                        method: "POST",
                        headers: {
                            "Content-Type": "application/json",
                            "Accept": "application/json"
                        },
                        body: JSON.stringify({ token: tokenInput.value })
                    });

                    const payload = await response.json().catch(function () { return null; });
                    if (response.ok && payload && payload.emailConfirmed) {
                        status.className = "success";
                        status.textContent = "Email confirmed for " + payload.email + ".";
                        return;
                    }

                    status.className = "error";
                    status.textContent = (payload && (payload.detail || payload.title)) || "Unable to confirm email.";
                    button.disabled = false;
                } catch (err) {
                    status.className = "error";
                    status.textContent = "Network error while confirming your email. Please try again.";
                    button.disabled = false;
                }
            });
        })();
    </script>
</body>
</html>
""";
    }

    public static string RenderMissingTokenPage()
    {
        return """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <meta name="referrer" content="no-referrer" />
    <title>Missing SmartLost confirmation token</title>
    <style>
        body {
            font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif;
            margin: 0;
            padding: 2rem 1rem;
            background: #f5f7fb;
            color: #162032;
        }
        main {
            max-width: 28rem;
            margin: 3rem auto;
            padding: 2rem;
            background: #ffffff;
            border: 1px solid #dce3ee;
            border-radius: 0.75rem;
        }
        h1 {
            font-size: 1.35rem;
            margin: 0 0 0.75rem;
        }
        p {
            line-height: 1.5;
            margin: 0;
            color: #b42318;
        }
    </style>
</head>
<body>
    <main>
        <h1>Invalid confirmation link</h1>
        <p>The confirmation link is missing its token parameter. Open the complete link from your email.</p>
    </main>
</body>
</html>
""";
    }
}
