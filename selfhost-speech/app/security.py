from __future__ import annotations

from ipaddress import ip_address, ip_network

from fastapi import Header, HTTPException, Request, status

from .config import Settings


def require_bearer_token(settings: Settings):
    async def _dependency(authorization: str | None = Header(default=None, alias="Authorization")) -> None:
        expected = settings.api_token.strip()
        if not expected:
            return

        if not authorization or not authorization.startswith("Bearer "):
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="Missing bearer token.",
                headers={"WWW-Authenticate": "Bearer"},
            )

        actual = authorization[len("Bearer ") :].strip()
        if actual != expected:
            raise HTTPException(
                status_code=status.HTTP_401_UNAUTHORIZED,
                detail="Invalid bearer token.",
                headers={"WWW-Authenticate": "Bearer"},
            )

    return _dependency


def require_ip_allowlist(settings: Settings):
    networks = []
    for raw in settings.ip_allowlist:
        try:
            if "/" in raw:
                networks.append(ip_network(raw, strict=False))
            else:
                suffix = "/128" if ":" in raw else "/32"
                networks.append(ip_network(f"{raw}{suffix}", strict=False))
        except ValueError:
            continue

    async def _dependency(request: Request) -> None:
        if not networks:
            return

        client_host = request.client.host if request.client else None
        if not client_host:
            raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="Client IP is unavailable.")

        try:
            client_ip = ip_address(client_host)
        except ValueError as exc:
            raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="Invalid client IP.") from exc

        if any(client_ip in net for net in networks):
            return

        raise HTTPException(status_code=status.HTTP_403_FORBIDDEN, detail="Client IP is not allowed.")

    return _dependency
