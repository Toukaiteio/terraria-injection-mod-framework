# lib/
#
# Vendored *reference* assemblies used for compilation only (not redistributed at runtime
# by the game). Game binaries must NOT be committed.
#
# Tracked:
#   xna/Microsoft.Xna.Framework*.dll   — XNA 4.0 GAC copies for CI / clean machines
#
# Gitignored:
#   Terraria.exe                       — place a copy of your 1.4.5.x client here, OR
#                                        set env TIMF_TERRARIA to its absolute path
#
# CI secrets (optional, enables full Core/UI/examples builds on GitHub-hosted runners):
#   TERRARIA_REF_URL    — HTTPS URL to a Terraria.exe you host privately (compile ref only)
#   TERRARIA_REF_TOKEN  — optional bearer token for that URL
# Self-hosted runners can simply place Terraria.exe here.
#
# Tag releases (push v*) REQUIRE a full build. Without TERRARIA_REF_URL / lib/Terraria.exe
# the CI smoke path still passes on PRs, but the release job refuses to publish a partial zip.
