#pragma once
#include <string>

class PipeClient
{
public:
    // Retourne true si la communication a réussi
    // 'allowed' est mis à jour avec la réponse du tray
    static bool Query(const std::wstring& jsonRequest, bool& allowed);
};
