#include "stdafx.h"
#include "PipeClient.h"
#include <windows.h>
#include <string>

static const wchar_t* PIPE_NAME = L"\\\\.\\pipe\\SafeExplorerPipe";

bool PipeClient::Query(const std::wstring& jsonRequest, bool& allowed)
{
    allowed = true; // fail-open

    HANDLE hPipe = CreateFileW(
        PIPE_NAME,
        GENERIC_READ | GENERIC_WRITE,
        0,
        nullptr,
        OPEN_EXISTING,
        0,
        nullptr);

    if (hPipe == INVALID_HANDLE_VALUE)
    {
        // Essaie le second nom de pipe si le premier échoue
        hPipe = CreateFileW(
            L"\\\\.\\pipe\\SafeDragIpcPipe",
            GENERIC_READ | GENERIC_WRITE,
            0,
            nullptr,
            OPEN_EXISTING,
            0,
            nullptr);

        if (hPipe == INVALID_HANDLE_VALUE)
            return false;
    }

    // Timeout court
    DWORD mode = PIPE_READMODE_BYTE;
    SetNamedPipeHandleState(hPipe, &mode, nullptr, nullptr);

    // Conversion wide -> UTF-8
    int sizeNeeded = WideCharToMultiByte(CP_UTF8, 0, jsonRequest.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (sizeNeeded <= 0)
    {
        CloseHandle(hPipe);
        return false;
    }

    std::string utf8(sizeNeeded, 0);
    WideCharToMultiByte(CP_UTF8, 0, jsonRequest.c_str(), -1, &utf8[0], sizeNeeded, nullptr, nullptr);

    // Enlève le null terminator pour l'écriture
    if (!utf8.empty() && utf8.back() == '\0')
        utf8.pop_back();

    DWORD written = 0;
    utf8.push_back('\n');
    BOOL ok = WriteFile(hPipe, utf8.data(), (DWORD)utf8.size(), &written, nullptr);
    if (!ok)
    {
        CloseHandle(hPipe);
        return false;
    }

    // Lecture de la réponse
    char buffer[512] = {};
    DWORD read = 0;
    ok = ReadFile(hPipe, buffer, sizeof(buffer) - 1, &read, nullptr);
    CloseHandle(hPipe);

    if (!ok || read == 0)
        return false;

    buffer[read] = '\0';
    std::string response(buffer);

    // Parsing très simple (robuste pour notre format)
    // On cherche "Allowed":true ou "Allowed": false
    if (response.find("\"Allowed\":true") != std::string::npos ||
        response.find("\"Allowed\": true") != std::string::npos)
    {
        allowed = true;
    }
    else if (response.find("\"Allowed\":false") != std::string::npos ||
             response.find("\"Allowed\": false") != std::string::npos)
    {
        allowed = false;
    }
    else
    {
        // Format inattendu -> fail-open
        allowed = true;
    }

    return true;
}
