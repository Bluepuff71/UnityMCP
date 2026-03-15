namespace UnityMCP.Editor.Core
{
    /// <summary>
    /// Provides embedded HTML content for MCP app widgets.
    /// </summary>
    public static class MCPApps
    {
        /// <summary>
        /// Gets the HTML content for the scene preview widget.
        /// </summary>
        public static string ScenePreviewHtml => ScenePreviewWidget;

        private const string ScenePreviewWidget = @"<!DOCTYPE html>
<html>
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<style>
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body {
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
    background: #1a1a2e; color: #e0e0e0;
    display: flex; flex-direction: column; align-items: center;
    min-height: 100vh; padding: 12px;
  }
  .header {
    display: flex; align-items: center; gap: 8px;
    margin-bottom: 8px; width: 100%; max-width: 800px;
  }
  .header h3 { font-size: 14px; color: #a0a0c0; flex: 1; }
  .refresh-btn {
    background: #2a2a4a; border: 1px solid #3a3a5a; color: #a0a0c0;
    padding: 4px 12px; border-radius: 4px; cursor: pointer; font-size: 12px;
  }
  .refresh-btn:hover { background: #3a3a5a; color: #fff; }
  .preview-container {
    width: 100%; max-width: 800px;
    background: #0d0d1a; border: 1px solid #2a2a4a; border-radius: 8px;
    overflow: hidden; position: relative;
  }
  .preview-container img {
    width: 100%; height: auto; display: block;
  }
  .placeholder {
    display: flex; align-items: center; justify-content: center;
    height: 300px; color: #606080; font-size: 14px;
  }
  .status {
    margin-top: 8px; font-size: 11px; color: #606080;
    width: 100%; max-width: 800px; text-align: right;
  }
</style>
</head>
<body>
  <div class=""header"">
    <h3>Unity Scene Preview</h3>
    <button class=""refresh-btn"" onclick=""refreshScreenshot()"">Refresh</button>
  </div>
  <div class=""preview-container"">
    <div id=""placeholder"" class=""placeholder"">Waiting for screenshot data...</div>
    <img id=""preview"" style=""display:none"" alt=""Scene Preview"">
  </div>
  <div id=""status"" class=""status""></div>

  <script>
    function refreshScreenshot() {
      if (window.callServerTool) {
        document.getElementById('status').textContent = 'Capturing...';
        window.callServerTool('vision_capture', {})
          .then(function(result) {
            updatePreview(result);
          })
          .catch(function(err) {
            document.getElementById('status').textContent = 'Error: ' + err.message;
          });
      } else {
        document.getElementById('status').textContent = 'Server tool API not available';
      }
    }

    function updatePreview(data) {
      var img = document.getElementById('preview');
      var placeholder = document.getElementById('placeholder');
      if (data && data.imageData) {
        img.src = 'data:image/png;base64,' + data.imageData;
        img.style.display = 'block';
        placeholder.style.display = 'none';
        document.getElementById('status').textContent = 'Updated: ' + new Date().toLocaleTimeString();
      }
    }

    // Listen for messages from the MCP client
    window.addEventListener('message', function(event) {
      if (event.data && event.data.type === 'toolResult') {
        updatePreview(event.data.result);
      }
    });
  </script>
</body>
</html>";
    }
}
