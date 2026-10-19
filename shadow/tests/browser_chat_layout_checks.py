"""Real-browser compact chat geometry and disclosure behavior checks.

The caller supplies a completed conversation with a plan and usage. No DOM content,
CSS rules, runtime state, or provider responses are replaced by these checks.
"""
import json

from playwright.sync_api import expect


def verify_compact_chat_layout(page, check, output, tag, attachment_entity):
    dock = page.locator('.agent-workspace')
    panel = page.locator('.agent-panel-chat')
    transcript = page.get_by_role('log', name='Agent conversation and tool activity')
    run = page.locator('details.agent-run-state')
    run_summary = page.locator('.agent-run-summary')
    run_popover = page.locator('.agent-run-popover')
    mode = page.locator('details.agent-mode-menu')
    mode_summary = page.locator('.agent-mode-toggle')
    mode_popover = page.locator('.agent-mode-popover')
    attachments = page.locator('details.agent-attachment-menu')
    attachment_summary = page.locator('.agent-attachment-toggle')
    attachment_popover = page.locator('.agent-attachment-popover')
    prompt = page.get_by_label('Message to coding agent', exact=True)
    metrics = []

    def settled():
        page.evaluate('() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))')

    def same_transcript(before, name):
        settled()
        after = transcript.bounding_box()
        check(name, all(abs(after[key] - before[key]) <= 1 for key in ('x', 'y', 'width', 'height')))

    def collapsed():
        expect(run).not_to_have_attribute('open', '')
        expect(mode).not_to_have_attribute('open', '')
        expect(run_popover).not_to_be_visible()
        expect(mode_popover).not_to_be_visible()
        expect(attachment_popover).not_to_be_visible()
        expect(page.get_by_label('Goal success criteria', exact=True)).not_to_be_visible()

    def inside_viewport(locator):
        value = locator.bounding_box()
        viewport = page.viewport_size
        return value and value['x'] >= -1 and value['y'] >= -1 and value['x'] + value['width'] <= viewport['width'] + 1 and value['y'] + value['height'] <= viewport['height'] + 1

    def capture(name):
        page.screenshot(path=str(output / f'{tag}-compact-{name}.png'), full_page=True)

    def inside_transcript(locator):
        value, area = locator.bounding_box(), transcript.bounding_box()
        return value and value['x'] >= area['x'] - 1 and value['y'] >= area['y'] - 1 and value['x'] + value['width'] <= area['x'] + area['width'] + 1 and value['y'] + value['height'] <= area['y'] + area['height'] + 1

    try:
        page.get_by_role('tab', name='Conversation', exact=True).click()
        collapsed()
        for name in ('dock-440x600', 'mobile-390x844', 'expanded-1600'):
            if name == 'dock-440x600':
                # Adjust only the browser viewport. The production ribbon/status bars
                # determine the remaining natural dock height; do not force panel CSS.
                page.set_viewport_size({'width': 1600, 'height': 1000})
                settled()
                for _ in range(3):
                    difference = 600 - dock.bounding_box()['height']
                    if abs(difference) <= 2:
                        break
                    page.set_viewport_size({'width': 1600, 'height': round(page.viewport_size['height'] + difference)})
                    settled()
                bounds = dock.bounding_box()
                check('compact chat uses the natural 440 by 600 dock', abs(bounds['width'] - 440) <= 2 and abs(bounds['height'] - 600) <= 2)
            elif name == 'mobile-390x844':
                page.set_viewport_size({'width': 390, 'height': 844})
            else:
                page.set_viewport_size({'width': 1600, 'height': 1000})
                page.get_by_role('button', name='Expand workspace', exact=True).click()
            settled()
            collapsed()
            conversation = transcript.bounding_box()
            area = panel.bounding_box()
            ratio = conversation['height'] / area['height']
            metrics.append({'layout': name, 'viewport': page.viewport_size, 'dock': dock.bounding_box(),
                            'chat': area, 'transcript': conversation, 'transcriptHeightRatio': ratio})
            (output / f'{tag}-compact-layout.json').write_text(json.dumps(metrics, indent=2) + '\n', encoding='utf-8')
            check(f'{name}: transcript occupies at least 65 percent of the conversation panel', ratio >= .65)
            toolbar = panel.locator('.agent-chat-toolbar').bounding_box()
            check(f'{name}: model, progress and context share one compact strip', toolbar['height'] <= 36 and run_summary.bounding_box()['y'] >= toolbar['y'] - 1 and panel.locator('.agent-context-meter').bounding_box()['y'] >= toolbar['y'] - 1)
            check(f'{name}: transcript and composer remain inside the viewport', inside_viewport(transcript) and inside_viewport(panel.locator('.agent-composer')))
            check(f'{name}: no persistent progress or mode overlay', not run.evaluate('(element) => element.open') and not mode.evaluate('(element) => element.open'))
            attach = page.get_by_role('button', name='Attach selected objects', exact=True)
            attach_bounds = attach.bounding_box()
            context = panel.locator('.agent-composer-context').bounding_box()
            check(f'{name}: selected drawing attachment is a compact composer icon',
                  attach_bounds['width'] <= 44 and attach_bounds['height'] <= 44
                  and attach_bounds['y'] >= context['y'] - 1 and attach_bounds['y'] + attach_bounds['height'] <= context['y'] + context['height'] + 1)
            expect(page.get_by_role('button', name='Send task', exact=True)).to_be_enabled()
            capture(name)

            run_summary.focus()
            page.keyboard.press('Enter')
            expect(run_popover).to_be_visible()
            same_transcript(conversation, f'{name}: opening progress does not shrink or move the transcript')
            check(f'{name}: progress popover stays over the transcript, away from Stop and composer', inside_transcript(run_popover))
            expect(run_popover.locator('.agent-plan')).to_be_visible()
            expect(run_popover.locator('.agent-usage-strip')).to_be_visible()
            expect(page.get_by_role('button', name='Import conversation', exact=True)).to_be_enabled()
            expect(page.get_by_role('button', name='Export conversation', exact=True)).to_be_enabled()
            capture(name + '-progress')
            run_popover.locator('.agent-usage-strip').focus()
            page.keyboard.press('Escape')
            expect(run_popover).not_to_be_visible()
            expect(run_summary).to_be_focused()
            check(f'{name}: Escape closes progress and restores summary focus')
            run_summary.click()
            page.get_by_role('button', name='Close run details', exact=True).click()
            expect(run_popover).not_to_be_visible()
            expect(run_summary).to_be_focused()
            check(f'{name}: progress Close control restores summary focus')

            mode_summary.focus()
            page.keyboard.press('Enter')
            expect(mode_popover).to_be_visible()
            expect(page.get_by_label('Agent execution mode', exact=True)).to_be_enabled()
            expect(page.get_by_label('Goal success criteria', exact=True)).to_be_visible()
            expect(page.get_by_role('button', name='Apply mode', exact=True)).to_be_visible()
            same_transcript(conversation, f'{name}: opening goal controls does not shrink or move the transcript')
            check(f'{name}: mode popover opens above its composer control', mode_popover.bounding_box()['y'] + mode_popover.bounding_box()['height'] <= mode_summary.bounding_box()['y'] + 2)
            check(f'{name}: mode popover is contained in the transcript', inside_transcript(mode_popover))
            capture(name + '-mode')
            page.get_by_label('Goal success criteria', exact=True).focus()
            page.keyboard.press('Escape')
            expect(mode_popover).not_to_be_visible()
            expect(mode_summary).to_be_focused()
            check(f'{name}: Escape closes goal controls and restores summary focus')
            mode_summary.click()
            page.get_by_role('button', name='Close task settings', exact=True).click()
            expect(mode_popover).not_to_be_visible()
            expect(mode_summary).to_be_focused()
            check(f'{name}: task settings Close control restores summary focus')

            run_summary.click()
            mode_summary.click()
            expect(run_popover).not_to_be_visible()
            expect(mode_popover).to_be_visible()
            check(f'{name}: opening mode controls closes the other popover')
            prompt.click()
            expect(mode_popover).not_to_be_visible()
            expect(prompt).to_be_focused()
            check(f'{name}: outside click dismisses mode controls without stealing composer focus')
            run_summary.click()
            prompt.click()
            expect(run_popover).not_to_be_visible()
            expect(prompt).to_be_focused()
            collapsed()
            same_transcript(conversation, f'{name}: dismissing popovers preserves conversation geometry')

            # Select an actual native circle created by this fixture. Capture and
            # remove it through production controls; no DOM/state injection.
            page.evaluate("id => shadowStudioAgentUi.execute('selection.set', {entities:[id]})", attachment_entity)
            attach.click()
            expect(attachment_summary).to_have_attribute('aria-label', 'Review 1 attached drawing object')
            expect(attachment_popover).not_to_be_visible()
            same_transcript(conversation, f'{name}: capturing selection adds no composer height')
            attachment_summary.focus()
            page.keyboard.press('Enter')
            expect(attachment_popover).to_be_visible()
            check(f'{name}: attachment popover stays inside transcript', inside_transcript(attachment_popover))
            attachment_popover.locator('.agent-reference>summary').click()
            expect(attachment_popover.locator('.agent-reference[open] pre')).to_be_visible()
            same_transcript(conversation, f'{name}: inspecting attachment metadata never shrinks chat')
            capture(name + '-attachments')
            mode_summary.click()
            expect(attachment_popover).not_to_be_visible()
            expect(mode_popover).to_be_visible()
            attachment_summary.click()
            expect(mode_popover).not_to_be_visible()
            page.keyboard.press('Escape')
            expect(attachment_popover).not_to_be_visible()
            expect(attachment_summary).to_be_focused()
            attachment_summary.click()
            remove = attachment_popover.get_by_role('button', name='Remove Circle ', exact=False)
            remove.focus()
            page.keyboard.press('Enter')
            expect(attachments).not_to_be_visible()
            expect(attach).to_be_focused()
            check(f'{name}: removing the final focused attachment restores Attach keyboard focus')
            same_transcript(conversation, f'{name}: attachment removal preserves transcript geometry')
            if name == 'expanded-1600':
                page.get_by_role('button', name='Restore docked workspace', exact=True).click()
    finally:
        page.set_viewport_size({'width': 1600, 'height': 1000})
