# QuicShare

> Send files quickly and securely

# This repo is no longer active. This project was a prototype for the [ZettaSend](https://zettasend.com) project, which is a professional transfer tool made for post-production workflows. You can download it from [zettasend.com](https://zettasend.com)

[![Download Now](https://img.shields.io/badge/Download%20Now-%23007ACC?style=for-the-badge&logo=github&logoColor=white)](https://github.com/zemendaniel/QuicShare/releases/latest)

A fast, lightweight, cross‑platform peer‑to‑peer file sharing app between two peers.  
Built with **.NET 9** and **Avalonia**.

QuicShare is simple to set up, easy to use, and designed to work on **Windows 11** and **Linux**.  
It’s perfect for sending large files to friends and family without paying for cloud storage.  
Just launch the app, share a room code, and send files directly between your devices.

---
<img width="1031" height="572" alt="Screenshot_20251117_155411" src="https://github.com/user-attachments/assets/28a2e4c5-10fc-4a4d-8a84-43bba3e6d929" />

---

## TL;DR

- **Direct peer‑to‑peer transfers** – no cloud storage, no central file server.
- **End‑to‑end encrypted** using QUIC + mTLS.
- **Unlimited file size** (only limited by your disk and network).
- **Both peers can send and receive** as long as they’re online at the same time.
- **Best experience with IPv6** (no manual port forwarding needed).
- **IPv4 with port forwarding** supported if IPv6 isn’t available.
- **Privacy‑friendly** – signaling server only exchanges connection metadata, not your files.

---

## Installation

### Windows 11 (x64 & ARM64)

1. Download the appropriate installer:
   - `QuicShareInstaller-v1.0-win-x64.exe` (for Windows 11 x64)
   - `QuicShareInstaller-v1.0-win-arm64.exe` (for Windows 11 ARM64)
2. Run the installer:
   - You may see **Windows Defender / SmartScreen** warnings because there is no code‑signing certificate yet.
   - Choose **“More info” → “Run anyway”** to continue.
3. After installation, launch **QuicShare** from the Start menu.

**Updating on Windows:**  
Download the latest installer and run it again. It will update your installation.

---

### Linux (Flatpak)

On Linux, QuicShare is distributed as a Flatpak.

#### One‑line install

```
bash <(wget -qO- https://raw.githubusercontent.com/zemendaniel/QuicShare/master/flatpak/install.sh)
```

After installation, run:

```
flatpak run io.github.zemendaniel.QuicShare
```

**Updating on Linux:**  
Re‑run the installer script above, or update via your Flatpak tooling as appropriate.  
You can also simply run the Flatpak installer again when a new version is released.

Alternative Linux download
If you prefer not to use Flatpak, you can alternatively download the standalone archive:
```
QuicShare-v1.0-linux-x64.zip
```
Extract it and run the *QuicShare* binary.

---

## Features

- 🔐 **Secure by design**
  - Uses **QUIC** with **mutual TLS (mTLS)** for transport‑level encryption.
  - Each peer exchanges certificate thumbprints via the signaling server.
  - File integrity is checked with a hashing algorithm after transfer.
- 🌍 **Cross‑platform**
  - **Windows 11** (x64 and ARM64).
  - **Linux x86_64** (Flatpak or self‑built).
- 📁 **Flexible file transfers**
  - No hard‑coded file size limit.
  - Both peers can send and receive an unlimited number of files.
  - Accept/reject each incoming file.
- 🌐 **Modern networking**
  - Uses **IPv6 by default** with UDP hole punching for seamless connectivity.
  - Supports **IPv4 + manual port forwarding** when IPv6 isn’t available.
  - Notifies you if IPv6 is missing so you can switch to IPv4 mode.
- ⚙️ **Configurable**
  - Toggle IPv4 usage and configure an IPv4 port.
  - Configure APIs used to detect your public IPv4/IPv6 addresses.
  - Configure signaling server URLs (including self‑hosted servers).
- 🧩 **Open source**
  - Licensed under **GPL**.
  - Public signaling server provided, and you can also **self‑host**.

---

## Platform Support

- ✅ **Windows 11**
  - Architectures: **x64** and **ARM64**.
- ✅ **Linux**
  - Architectures: **x86_64** via Flatpak or manual build.
- ⚠️ **Windows 10 is not supported**
  - `msquic.dll` could not be made to work reliably on Windows 10.
- ⚠️ **macOS is not supported**
  - `msquic.dll` could not be made to work reliably on macOS. 
    - But with some help from the community, it should be possible to build a macOS version.

---
<img width="1031" height="572" alt="Screenshot_20251117_155441" src="https://github.com/user-attachments/assets/59ac6a07-61d0-4f88-ae1d-dcbb43ccc7e6" />
<img width="1031" height="572" alt="Screenshot_20251117_155648" src="https://github.com/user-attachments/assets/ece8f85b-ca61-45ac-acee-c2b11fa25a6f" />

---

## How It Works (Networking & Protocols)

### Signaling

- QuicShare uses a **signaling server** to help two peers find each other and exchange connection info.
- Public signaling server implementation:  
  https://github.com/zemendaniel/signaling-server
- You can: 
  - Use the **public signaling server** (default URLs are preconfigured in the app settings).
  - **Self‑host** the signaling server for maximum privacy and control, following the instructions in that repo.
- The signaling server sees: 
  - Your room code.
  - Certificates’ **thumbprints** for mutual verification.
  - Your IPs and port.
- **Files never flow through the signaling server** – they go directly between peers.
- **No data is stored on the signaling server**, it simply forwards the peer discovery messages.

### Connection Establishment

1. Peer A creates a room in QuicShare and gets a **room code**.
2. Peer A shares this code with Peer B (e.g., Discord, email, phone).
3. Peer B enters the room code and joins.
4. Both peers contact the signaling server to:
   - Exchange **peer addresses**.
   - Exchange **certificate thumbprints** for mTLS.
5. Once the direct QUIC connection is established, the signaling server is no longer involved:
   - All traffic is **end‑to‑end** between the two peers.
   - The connection is **encrypted**.

### IPv6 vs IPv4

- **IPv6 (recommended)**
  - Used by default for **end‑to‑end connectivity**.
  - Works best when both peers have proper IPv6 connectivity.
  - No manual port forwarding typically needed.
  - You can check whether you have IPv6 at: 
    - https://whatismyipaddress.com
- **IPv4 + Port Forwarding**
  - If you or your peer doesn’t have IPv6, you can fall back to IPv4.
  - One peer must configure **UDP port forwarding** on their router: 
    - Forward a chosen UDP port (configurable in QuicShare settings) to the local machine running QuicShare.
  - The protocol is **symmetric**, so either side can be the one that forwards.
  - The app will **notify you** if IPv6 is unavailable and you should configure IPv4 / port forwarding.

---

## Usage

1. **Create a room**
   - One person clicks **“Create Room”** in QuicShare.
   - The app shows a **room code**.
2. **Share the room code**
   - Send this code to your peer via any channel: 
     - Discord, email, SMS, phone call, etc.
3. **Join the room**
   - The other person opens QuicShare and selects **“Join Room”**.
   - They enter the **room code** and confirm.
   - Wait a few seconds; the app will notify both of you when you’re in the same room and the connection is established.
4. **Send files**
   - Either peer can now select files to send.
   - The other peer can **accept** or **reject** each incoming file.
   - Transfers are encrypted and verified with a hash for integrity.

Both peers must remain **online and in the app** during the transfer.

---

## Settings

You can configure:

- **Networking**
  - Enable/disable **IPv4** usage.
  - Set the **IPv4 UDP port** for incoming connections (useful for port forwarding).
- **Address detection**
  - URLs/APIs used to determine your public **IPv4** and **IPv6** addresses.
- **Signaling server**
  - The default public signaling server URLs are preconfigured in the app.
  - You can override these with your **own self‑hosted signaling server** URLs. **Make sure you and your peer are using the same signaling server.**

---

## Security & Privacy

- **End‑to‑end encryption**
  - Connections use **QUIC** with **mutual TLS (mTLS)**.
  - Both client and server validate each other using **certificate thumbprints** exchanged via the signaling server.
- **No central file storage**
  - Files are sent **directly between peers**.
  - The signaling server is only used for: 
    - Room coordination.
    - Exchanging connection metadata and certificate thumbprints.
  - File data **never passes through the signaling server**.
- **Data integrity**
  - A **hashing algorithm** verifies that received files match the originals.


For maximum privacy, you can self‑host the signaling server and configure QuicShare to use it.

---

## Roadmap

- Improve file sending UX: 
  - Drag‑and‑drop support.
- Transfer controls: 
  - Pause, resume later, and cancel.
- UX & design: 
  - Improve visual design and theming.

---

## Contributing

Contributions are welcome!

- If you want to help with features, bug fixes, or refactoring, feel free to open a **pull request**.
- If you’d like parts of the code explained or want guidance on where to start, please reach out via **GitHub Discussions**.

### Questions, Support, and Bugs

- **Questions / general discussion:**  
  https://github.com/zemendaniel/QuicShare/discussions
- **Bugs, feature requests, issues:**  
  Open an **issue** here:  
  https://github.com/zemendaniel/QuicShare/issues

And if you find QuicShare useful, **please star the repo ⭐** – it really helps!

---

## License

QuicShare is licensed under the **GNU General Public License (GPL)**.  
See the `LICENSE` file for details.
