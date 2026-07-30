#include "stdafx.h"
#include "CopyHook.h"
#include "PipeClient.h"
#include <string>

UINT CCopyHook::CopyCallback(
    HWND /*hwnd*/,
    UINT wFunc,
    UINT /*wFlags*/,
    LPCWSTR pszSrcFile,
    DWORD /*dwSrcAttribs*/,
    LPCWSTR pszDestFile,
    DWORD /*dwDestAttribs*/)
{
    // On ne s'intéresse qu'aux déplacements (et éventuellement copies)
    if (wFunc != FO_MOVE && wFunc != FO_COPY)
        return IDYES;

    if (!pszSrcFile || !pszDestFile)
        return IDYES;

    // OutputDebugStringW pour le suivi Sysinternals DebugView
    std::wstring debugMsg = L"[SafeExplorer] CopyCallback: " + std::wstring(pszSrcFile) + L" -> " + std::wstring(pszDestFile) + L"\n";
    OutputDebugStringW(debugMsg.c_str());

    // Construction de la requête JSON simple
    std::wstring src(pszSrcFile);
    std::wstring dest(pszDestFile);

    // Échapper les backslash pour JSON
    auto escape = [](const std::wstring& s) -> std::wstring {
        std::wstring r;
        for (wchar_t c : s) {
            if (c == L'\\') r += L"\\\\";
            else if (c == L'"') r += L"\\\"";
            else r += c;
        }
        return r;
    };

    int operation = (wFunc == FO_MOVE) ? 1 : 0; // 1 = Move, 0 = Copy

    std::wstring json =
        L"{\"SourcePath\":\"" + escape(src) +
        L"\",\"DestinationPath\":\"" + escape(dest) +
        L"\",\"Operation\":" + std::to_wstring(operation) + L"}";

    bool allowed = true; // fail-open par défaut

    // Interroge le named pipe
    if (PipeClient::Query(json, allowed))
    {
        // allowed a été mis à jour par le Named Pipe
    }
    else
    {
        // Pipe indisponible -> on autorise (sécurité fail-open)
        allowed = true;
    }

    return allowed ? IDYES : IDNO;
}
