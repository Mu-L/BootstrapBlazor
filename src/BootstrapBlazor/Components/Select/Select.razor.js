﻿import { getTransitionDelayDurationFromElement } from "../../modules/utility.js"
import { registerSelect, unregisterSelect } from "../../modules/base-select.js"
import Data from "../../modules/data.js"
import Popover from "../../modules/base-popover.js"

export function init(id, invoke, options) {
    const el = document.getElementById(id)
    if (el === null) {
        return
    }

    const search = el.querySelector(".search-text")
    const popover = Popover.init(el, {
        hideCallback: () => {
            if (options.triggerCollapsed) {
                invoke.invokeMethodAsync(options.triggerCollapsed);
            }
        }
    });
    const input = el.querySelector(`#${id}_input`);
    const select = {
        el, invoke, options,
        search,
        keydownEl: [search, input],
        popover
    }
    Data.set(id, select);
    registerSelect(select);
}

export function show(id) {
    const select = Data.get(id)
    if (select) {
        const delay = getTransitionDelayDurationFromElement(select.popover.toggleElement);
        const handler = setTimeout(() => {
            clearTimeout(handler);
            select.popover.show();
        }, delay);
    }
}

export function hide(id) {
    const select = Data.get(id)
    const delay = getTransitionDelayDurationFromElement(select.popover.toggleElement);
    if (select) {
        const handler = setTimeout(() => {
            clearTimeout(handler);
            select.popover.hide();
        }, delay)
    }
}

export function resetValue(id, value) {
    const input = document.getElementById(id);
    if (input) {
        input.value = value;
    }
}

export function dispose(id) {
    const select = Data.get(id)
    Data.remove(id)

    if (select) {
        unregisterSelect(select);
        if (select.popover) {
            Popover.dispose(select.popover);
        }
    }
}
