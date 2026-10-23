$ErrorActionPreference = 'Stop'
try {
    $request = [Console]::In.ReadToEnd() | ConvertFrom-Json
    if ($request.toolCall.name -ceq 'finish') {
        [Console]::Out.Write('{"decision":"allow"}')
    } else {
        [Console]::Out.Write('{"decision":"deny","reason":"AIVSP text inference does not permit tools."}')
    }
} catch {
    [Console]::Out.Write('{"decision":"deny","reason":"Invalid tool request."}')
}
