#!/usr/bin/env python3
"""Task 10.4/D14: tela de progresso da instalação, com a identidade do
próprio app. Lê marcadores de "install-fifo" (escrito por common.sh
emit_progress), resolve texto via strings.linux.json (10.6 - gerado a
partir dos .resx / verificado contra eles), e nunca tem autoridade sobre o
registro transacional: só apresenta o que o instalador já decidiu.

Roda sob Xorg (task 1.8) sem gerenciador de janelas: esta janela cobre a
tela inteira sozinha.
"""
import json
import os
import sys
import threading
import tkinter as tk

CATALOG_PATH = "/opt/linuxhub/catalog/presentation-catalog.json"
STRINGS_PATH = "/opt/linuxhub/catalog/strings.linux.json"
FIFO_PATH = "/run/linuxhub-progress.fifo"
DEFAULT_LOCALE = "en-US"


def load_json(path, default):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except (OSError, json.JSONDecodeError):
        return default


def resolve_text(strings, text_key, locale):
    entry = strings.get(text_key, {})
    return entry.get(locale) or entry.get(DEFAULT_LOCALE) or text_key


class ProgressWindow:
    def __init__(self, root, catalog, strings, locale):
        self.catalog = catalog.get("markers", {})
        self.strings = strings
        self.locale = locale
        self.root = root
        root.configure(bg="#0b0b0f")
        root.attributes("-fullscreen", True)

        self.title_label = tk.Label(
            root, text="LinuxHub", fg="#e8e8ec", bg="#0b0b0f", font=("DejaVu Sans", 28, "bold")
        )
        self.title_label.pack(pady=(120, 20))

        self.status_label = tk.Label(
            root, text="", fg="#c7c7ce", bg="#0b0b0f", font=("DejaVu Sans", 16)
        )
        self.status_label.pack(pady=10)

        self.canvas = tk.Canvas(root, width=600, height=24, bg="#1c1c22", highlightthickness=0)
        self.canvas.pack(pady=20)
        self.bar = self.canvas.create_rectangle(0, 0, 0, 24, fill="#4c8bf5", width=0)

    def set_progress(self, marker, detail_percent):
        entry = self.catalog.get(marker)
        if entry is None:
            # Task 9.8/D14: marcador desconhecido interrompe a instalação,
            # não vira tela em branco. A UI só reporta; quem interrompe de
            # fato é o bash (emit_progress já morre antes de escrever aqui).
            self.status_label.config(text=f"Marcador de progresso desconhecido: {marker}")
            return

        text = resolve_text(self.strings, entry["textKey"], self.locale)
        if "percentStart" in entry and detail_percent not in (None, ""):
            try:
                detail = max(0, min(100, int(detail_percent)))
            except ValueError:
                detail = 0
            span = entry["percentEnd"] - entry["percentStart"]
            percent = entry["percentStart"] + (span * detail // 100)
        else:
            percent = entry.get("percent", 0)

        self.status_label.config(text=text)
        width = int(600 * percent / 100)
        self.canvas.coords(self.bar, 0, 0, width, 24)


def fifo_reader(window, root):
    try:
        if not os.path.exists(FIFO_PATH):
            os.mkfifo(FIFO_PATH, 0o600)
    except OSError:
        pass
    while True:
        try:
            with open(FIFO_PATH, "r", encoding="utf-8") as fifo:
                for line in fifo:
                    line = line.rstrip("\n")
                    if not line:
                        continue
                    parts = line.split("\t", 1)
                    marker = parts[0]
                    detail = parts[1] if len(parts) > 1 else ""
                    root.after(0, window.set_progress, marker, detail)
        except OSError:
            pass


def main():
    locale = os.environ.get("LINUXHUB_LOCALE", DEFAULT_LOCALE)
    catalog = load_json(CATALOG_PATH, {"markers": {}})
    strings = load_json(STRINGS_PATH, {})

    root = tk.Tk()
    window = ProgressWindow(root, catalog, strings, locale)

    thread = threading.Thread(target=fifo_reader, args=(window, root), daemon=True)
    thread.start()

    root.mainloop()


if __name__ == "__main__":
    sys.exit(main())
