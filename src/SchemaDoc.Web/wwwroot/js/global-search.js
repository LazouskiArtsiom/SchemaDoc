window.globalSearch = {
    _dotNetRef: null,
    init: function (dotNetRef) {
        this._dotNetRef = dotNetRef;
        if (!this._listening) {
            this._listening = true;
            document.addEventListener('keydown', function (e) {
                if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
                    e.preventDefault();
                    if (window.globalSearch._dotNetRef) {
                        window.globalSearch._dotNetRef.invokeMethodAsync('OnCtrlK');
                    }
                }
            });
        }
    },
    focusInput: function (elementId) {
        setTimeout(function () {
            var el = document.getElementById(elementId);
            if (el) el.focus();
        }, 50);
    }
};
