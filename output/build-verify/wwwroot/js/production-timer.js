window.productionTimer = window.productionTimer || {
    _audioContext: null,

    async playAlarm() {
        try {
            const AudioContextCtor = window.AudioContext || window.webkitAudioContext;
            if (!AudioContextCtor) {
                return;
            }

            this._audioContext ??= new AudioContextCtor();
            if (this._audioContext.state === "suspended") {
                await this._audioContext.resume();
            }

            const now = this._audioContext.currentTime;
            const pattern = [0, 0.24, 0.48];

            pattern.forEach((offset, index) => {
                const oscillator = this._audioContext.createOscillator();
                const gainNode = this._audioContext.createGain();

                oscillator.type = "sine";
                oscillator.frequency.value = index === 1 ? 932.33 : 783.99;

                gainNode.gain.setValueAtTime(0.0001, now + offset);
                gainNode.gain.exponentialRampToValueAtTime(0.16, now + offset + 0.02);
                gainNode.gain.exponentialRampToValueAtTime(0.0001, now + offset + 0.18);

                oscillator.connect(gainNode);
                gainNode.connect(this._audioContext.destination);
                oscillator.start(now + offset);
                oscillator.stop(now + offset + 0.2);
            });
        } catch {
        }
    }
};
