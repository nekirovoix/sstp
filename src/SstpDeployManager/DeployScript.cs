namespace SstpDeployManager;

internal static class DeployScript
{
    public static string Text => """
#!/bin/bash
set -u
if [ "$(id -u)" -ne 0 ]; then echo "ERROR: Run as root or configure passwordless sudo."; exit 10; fi
CFG="$1"
[ -f "$CFG" ] || { echo "ERROR: Configuration file missing"; exit 11; }
. "$CFG"
DOMAIN=$(printf '%s' "$DOMAIN_B64" | base64 -d)
VPN_USER=$(printf '%s' "$VPN_USER_B64" | base64 -d)
VPN_PASS=$(printf '%s' "$VPN_PASS_B64" | base64 -d)
rm -f "$CFG"
case "$DOMAIN" in *[!A-Za-z0-9.-]*|'') echo 'ERROR: Invalid domain'; exit 12;; esac
case "$VPN_USER" in *[!A-Za-z0-9._-]*|'') echo 'ERROR: Invalid VPN username'; exit 13;; esac
export DEBIAN_FRONTEND=noninteractive
echo '[1/7] Installing prerequisites...'
apt-get update -qq
apt-get install -y -qq build-essential curl openssl ca-certificates certbot iptables >/var/log/sstp-deployer-packages.log
VPNCMD=/usr/local/vpnserver/vpncmd
JUST_INSTALLED=0
if [ ! -x /usr/local/vpnserver/vpnserver ]; then
  JUST_INSTALLED=1
  echo '[2/7] Installing SoftEther VPN Server...'
  cd /root
  rm -rf /root/vpnserver /root/softether-vpnserver.tar.gz
  curl -fsSL --retry 3 -o /root/softether-vpnserver.tar.gz https://github.com/SoftEtherVPN/SoftEtherVPN_Stable/releases/download/v4.41-9787-rtm/softether-vpnserver-v4.41-9787-rtm-2023.03.14-linux-x64-64bit.tar.gz
  echo 'c766f92fc664cc95eb4b78d7c041a1e26dbd20d2e714438426c7bc45b146ffd9  /root/softether-vpnserver.tar.gz' | sha256sum -c -
  tar -xzf /root/softether-vpnserver.tar.gz -C /root
  cd /root/vpnserver
  printf '1\n1\n1\n' | make >/var/log/softether-make.log
  install -d -m 0700 /usr/local/vpnserver
  cp -a . /usr/local/vpnserver/
  chmod 600 /usr/local/vpnserver/*
  chmod 700 /usr/local/vpnserver/vpnserver /usr/local/vpnserver/vpncmd
  cat >/etc/systemd/system/softether-vpnserver.service <<'UNIT'
[Unit]
Description=SoftEther VPN Server
After=network-online.target
Wants=network-online.target
[Service]
Type=forking
ExecStart=/usr/local/vpnserver/vpnserver start
ExecStop=/usr/local/vpnserver/vpnserver stop
WorkingDirectory=/usr/local/vpnserver
Restart=on-failure
[Install]
WantedBy=multi-user.target
UNIT
  systemctl daemon-reload
  systemctl enable --now softether-vpnserver
  sleep 3
else
  echo '[2/7] SoftEther already installed.'
  systemctl enable --now softether-vpnserver >/dev/null
fi
SECRETS=/root/.sstp-deployer-secrets
MANAGED=0
if [ -f /root/sstp-credentials.txt ]; then
  ADMIN_PASS=$(sed -n 's/^SoftEther server admin password: //p' /root/sstp-credentials.txt)
  HUB_PASS=$(sed -n 's/^SoftEther hub admin password: //p' /root/sstp-credentials.txt)
  MANAGED=1
elif [ -f "$SECRETS" ]; then
  . "$SECRETS"
  MANAGED=1
elif [ "$JUST_INSTALLED" = 1 ]; then
  ADMIN_PASS=$(openssl rand -hex 12)
  HUB_PASS=$(openssl rand -hex 12)
  printf 'ADMIN_PASS=%s\nHUB_PASS=%s\n' "$ADMIN_PASS" "$HUB_PASS" >"$SECRETS"
  chmod 600 "$SECRETS"
  MANAGED=1
else
  echo 'ERROR: SoftEther already exists but its administrator password is not managed by this program.'
  exit 20
fi
MGMT=localhost:443
sv(){ timeout 20 "$VPNCMD" "$MGMT" /SERVER /PASSWORD:"$ADMIN_PASS" "$@" </dev/null; }
hub(){ timeout 20 "$VPNCMD" "$MGMT" /SERVER /HUB:SSTP /PASSWORD:"$HUB_PASS" "$@" </dev/null; }
if sv /CMD ServerInfoGet >/dev/null 2>&1; then
  echo '[3/7] SoftEther administrator verified on TCP 443.'
else
  MGMT=localhost:5555
  if sv /CMD ServerInfoGet >/dev/null 2>&1; then
    echo '[3/7] SoftEther administrator verified on management port 5555.'
  else
    echo '[3/7] Waiting for SoftEther management port 5555...'
    READY=0
    for i in $(seq 1 30); do
      if timeout 5 "$VPNCMD" localhost:5555 /SERVER /CMD ServerInfoGet </dev/null >/dev/null 2>&1; then READY=1; break; fi
      sleep 1
    done
    if [ "$READY" != 1 ]; then
      echo '[3/7] Management interface was not ready; repairing the deployer-managed configuration...'
      if [ "$MANAGED" != 1 ]; then echo 'ERROR: Existing SoftEther administrator password is unknown.'; exit 20; fi
      systemctl stop softether-vpnserver
      if [ -f /usr/local/vpnserver/vpn_server.config ]; then
        mv /usr/local/vpnserver/vpn_server.config "/usr/local/vpnserver/vpn_server.config.backup.$(date +%s)"
      fi
      systemctl start softether-vpnserver
      READY=0
      for i in $(seq 1 45); do
        if timeout 5 "$VPNCMD" localhost:5555 /SERVER /CMD ServerInfoGet </dev/null >/dev/null 2>&1; then READY=1; break; fi
        sleep 1
      done
      [ "$READY" = 1 ] || { echo 'ERROR: SoftEther management port 5555 did not become ready.'; exit 20; }
    fi
    echo '[3/7] Initializing SoftEther administrator on port 5555...'
    timeout 20 "$VPNCMD" localhost:5555 /SERVER /CMD ServerPasswordSet "$ADMIN_PASS" </dev/null >/var/log/sstp-admin-init.log 2>&1 || true
    for i in $(seq 1 15); do sv /CMD ServerInfoGet >/dev/null 2>&1 && break; sleep 1; done
    sv /CMD ServerInfoGet >/dev/null 2>&1 || { echo 'ERROR: Could not initialize SoftEther administrator. See /var/log/sstp-admin-init.log'; exit 20; }
  fi
fi
if ! sv /CMD HubList 2>/dev/null | grep -q '|SSTP'; then
  sv /CMD HubCreate SSTP /PASSWORD:"$HUB_PASS" >/dev/null 2>&1 || true
fi
echo '[4/7] Creating SSTP user and SecureNAT...'
hub /CMD UserCreate "$VPN_USER" /GROUP:none /REALNAME:none /NOTE:none >/dev/null 2>&1 || true
hub /CMD UserPasswordSet "$VPN_USER" /PASSWORD:"$VPN_PASS" >/dev/null 2>&1 || { echo 'ERROR: Could not set VPN password.'; exit 21; }
hub /CMD SecureNatEnable >/dev/null 2>&1 || true
PUBLIC_IP=$(curl -4fsS --max-time 10 https://api.ipify.org || true)
DNS_IP=$(getent ahostsv4 "$DOMAIN" | awk 'NR==1{print $1}')
echo "[5/7] DNS: $DOMAIN -> ${DNS_IP:-unknown}; server public IP: ${PUBLIC_IP:-unknown}"
if [ -n "$PUBLIC_IP" ] && [ "$DNS_IP" != "$PUBLIC_IP" ]; then echo 'ERROR: Domain does not point to this server.'; exit 22; fi
if ss -lntp | grep -qE ':80 .*users:'; then echo 'ERROR: TCP port 80 is occupied; Certbot standalone cannot validate the domain.'; exit 23; fi
certbot certonly --standalone --preferred-challenges http -d "$DOMAIN" --agree-tos --register-unsafely-without-email --non-interactive --keep-until-expiring || { echo 'ERROR: Certbot could not obtain or reuse the certificate.'; exit 34; }
# Enable SSTP before installing the public certificate. Some SoftEther builds
# regenerate their default certificate while enabling the SSTP clone server.
SSTP_OUT=$(sv /CMD SstpEnable yes 2>&1 || true)
printf '%s' "$SSTP_OUT" | grep -qi 'completed successfully' || { echo 'ERROR: Could not enable SSTP.'; exit 30; }
# SoftEther Stable expects the leaf certificate and intermediate chain separately.
install -d -m 700 /usr/local/vpnserver/chain_certs
cp -L "/etc/letsencrypt/live/$DOMAIN/chain.pem" /usr/local/vpnserver/chain_certs/letsencrypt-chain.pem
chmod 600 /usr/local/vpnserver/chain_certs/letsencrypt-chain.pem
CERT_LOG=/var/log/sstp-cert-import.log
CERT_OUT=$(sv /CMD ServerCertSet /LOADCERT:"/etc/letsencrypt/live/$DOMAIN/cert.pem" /LOADKEY:"/etc/letsencrypt/live/$DOMAIN/privkey.pem" 2>&1 || true)
printf '%s\n' "$CERT_OUT" >"$CERT_LOG"
printf '%s' "$CERT_OUT" | grep -qi 'completed successfully' || { echo 'ERROR: Could not import the Let’s Encrypt certificate. See /var/log/sstp-cert-import.log'; exit 29; }
for p in 992 1194; do sv /CMD ListenerDisable "$p" >/dev/null 2>&1 || true; done
# Apply the new certificate and test whether vpncmd management is multiplexed on TCP 443.
systemctl restart softether-vpnserver
sleep 3
MGMT=localhost:443
MGMT443=0
for i in $(seq 1 45); do
  if sv /CMD ServerInfoGet >/dev/null 2>&1; then MGMT443=1; break; fi
  sleep 1
done
if [ "$MGMT443" = 1 ]; then
  echo '[5/7] Management on TCP 443 verified; closing external port 5555.'
  sv /CMD ListenerDisable 5555 >/dev/null 2>&1 || true
else
  echo '[5/7] TCP 443 is SSTP-only; keeping port 5555 for localhost management and blocking it externally.'
  MGMT=localhost:5555
  for i in $(seq 1 20); do sv /CMD ServerInfoGet >/dev/null 2>&1 && break; sleep 1; done
  sv /CMD ServerInfoGet >/dev/null 2>&1 || { echo 'ERROR: Local SoftEther management is unavailable on both 443 and 5555.'; exit 28; }
fi
# Port 5555 may remain for local administration, but never expose it on the network.
iptables -C INPUT ! -i lo -p tcp --dport 5555 -j DROP 2>/dev/null || iptables -I INPUT 1 ! -i lo -p tcp --dport 5555 -j DROP
cat >/etc/systemd/system/softether-local-mgmt-firewall.service <<'FWUNIT'
[Unit]
Description=Restrict SoftEther management port 5555 to localhost
After=network.target
[Service]
Type=oneshot
ExecStart=/bin/sh -c '/usr/sbin/iptables -C INPUT ! -i lo -p tcp --dport 5555 -j DROP 2>/dev/null || /usr/sbin/iptables -I INPUT 1 ! -i lo -p tcp --dport 5555 -j DROP'
RemainAfterExit=yes
[Install]
WantedBy=multi-user.target
FWUNIT
systemctl daemon-reload
systemctl enable --now softether-local-mgmt-firewall.service >/dev/null
echo '[5/7] Verifying the certificate actually served on TCP 443...'
SERVED_CERT=$(mktemp)
if ! timeout 15 openssl s_client -connect 127.0.0.1:443 -servername "$DOMAIN" -showcerts </dev/null 2>/dev/null | openssl x509 -outform PEM >"$SERVED_CERT"; then
  rm -f "$SERVED_CERT"; echo 'ERROR: Could not read the certificate served on TCP 443.'; exit 31
fi
EXPECTED_FP=$(openssl x509 -in "/etc/letsencrypt/live/$DOMAIN/cert.pem" -noout -fingerprint -sha256 | cut -d= -f2)
SERVED_FP=$(openssl x509 -in "$SERVED_CERT" -noout -fingerprint -sha256 | cut -d= -f2)
HOST_CHECK=$(openssl x509 -in "$SERVED_CERT" -noout -checkhost "$DOMAIN" 2>&1 || true)
if [ -z "$EXPECTED_FP" ] || [ "$SERVED_FP" != "$EXPECTED_FP" ] || ! printf '%s' "$HOST_CHECK" | grep -qi 'does match certificate'; then
  openssl x509 -in "$SERVED_CERT" -noout -subject -issuer
  rm -f "$SERVED_CERT"; echo 'ERROR: TCP 443 is still serving the old/self-signed certificate.'; exit 32
fi
if ! timeout 15 openssl s_client -connect 127.0.0.1:443 -servername "$DOMAIN" -verify_return_error </dev/null 2>&1 | grep -q 'Verify return code: 0 (ok)'; then
  rm -f "$SERVED_CERT"; echo 'ERROR: TCP 443 is not serving a complete trusted certificate chain.'; exit 33
fi
rm -f "$SERVED_CERT"
echo '[6/7] Configuring automatic certificate renewal...'
cat >/etc/letsencrypt/renewal-hooks/deploy/softether-sstp.sh <<HOOK
#!/bin/bash
ADMIN_PASS='$ADMIN_PASS'
for ENDPOINT in localhost:443 localhost:5555; do
  OUT=\$(timeout 20 /usr/local/vpnserver/vpncmd "\$ENDPOINT" /SERVER /PASSWORD:"\$ADMIN_PASS" /CMD ServerCertSet /LOADCERT:/etc/letsencrypt/live/$DOMAIN/cert.pem /LOADKEY:/etc/letsencrypt/live/$DOMAIN/privkey.pem </dev/null 2>&1 || true)
  printf '%s' "\$OUT" | grep -qi 'completed successfully' && break
done
install -d -m 700 /usr/local/vpnserver/chain_certs
cp -L /etc/letsencrypt/live/$DOMAIN/chain.pem /usr/local/vpnserver/chain_certs/letsencrypt-chain.pem
chmod 600 /usr/local/vpnserver/chain_certs/letsencrypt-chain.pem
systemctl restart softether-vpnserver
HOOK
chmod 700 /etc/letsencrypt/renewal-hooks/deploy/softether-sstp.sh
cat >/root/sstp-credentials.txt <<CREDS
SSTP server: $DOMAIN
VPN type: SSTP
Username: $VPN_USER@SSTP
Password: $VPN_PASS
SoftEther server admin password: $ADMIN_PASS
SoftEther hub admin password: $HUB_PASS
CREDS
chmod 600 /root/sstp-credentials.txt
echo '[7/7] Verifying service...'
systemctl is-active --quiet softether-vpnserver || { echo 'ERROR: SoftEther service is not active.'; exit 24; }
ss -lntp | grep -q ':443 ' || { echo 'ERROR: TCP 443 is not listening.'; exit 25; }
S=$(sv /CMD SstpGet 2>&1 || true); echo "$S" | grep -q '|Yes' || { echo 'ERROR: SSTP is not enabled.'; exit 26; }
U=$(hub /CMD UserList 2>&1 || true); echo "$U" | grep -q "$VPN_USER" || { echo 'ERROR: VPN user is missing.'; exit 27; }
echo "DEPLOY_OK: SSTP is ready at $DOMAIN:443 for $VPN_USER@SSTP"
""";
}
